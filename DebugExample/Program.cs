using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using DebuggerLib;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using DebugStatement = System.ValueTuple<Microsoft.CodeAnalysis.CSharp.Syntax.StatementSyntax, string[]>;

namespace debug
{
    class Program
    {
        private static IEnumerable<MetadataReference> References() => ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            .Split(Path.PathSeparator).Select(refs => MetadataReference.CreateFromFile(refs)).Cast<MetadataReference>().ToList();


        private const string SourcePath = "../../../../Example/Program.cs";

        static void Main(string[] args)
        {
            Run();
        }

        static Dictionary<int, string> statments = new Dictionary<int, string>();

        public static void Run()
        {
            // Generates the code for debugging.
            var sourceCode = File.ReadAllText(SourcePath);
            var (assembly, statmentIndexes) = Build(sourceCode);

            statments = statmentIndexes.GroupBy(x => x.Item1).ToDictionary(x => x.Key, x => x.First().Item2);
            DebugHelper.BreakPoints = statments.Select(x => x.Key).ToDictionary(x => x, t => false);
            DebugMenu(Array.Empty<Var>(), sourceCode);

            DebugHelper.BreakPointHit += (spanStart, spanLength, variables) =>
            {
                DebugMenu(variables, sourceCode);
                DebugHelper.Lock = false;
            };

            // Calls the Main method.
            var entryPoint = assembly.EntryPoint;
            entryPoint.Invoke(null, new object[] { new string[0] });

            Console.Read();
        }

        private static void DebugMenu(Var[] variables, string source)
        {
            while (true)
            {
                Console.WriteLine("BreakPoint Hit values");
                Console.WriteLine(string.Join(", ", variables.Select(v => $"{v.Name}: {v.Value}")));
                PrintStatmentOptions();
                Console.WriteLine("1 remove all break points");
                Console.WriteLine("2 add/remove break point");
                Console.WriteLine("3 continue");
                var line = Console.ReadLine();
                switch (int.Parse(line))
                {
                    case 1:
                        DebugHelper.BreakPoints.Clear();
                        continue;
                    case 2:

                        var index = GetIndex();
                        DebugHelper.BreakPoints[index] = !DebugHelper.BreakPoints[index];
                        continue;

                    case 3:
                        return;
                }

            }
        }

        private static void PrintStatmentOptions()
        {
            Console.WriteLine(Environment.NewLine);
            Console.WriteLine($"Index\tBreakPointSet\tCode");
            foreach (var items in DebugHelper.BreakPoints)
            {
                Console.WriteLine($"{items.Key}\t{items.Value}\t{statments[items.Key]}");
            }
        }

        private static int GetIndex()
        {
            while (true)
            {
                Console.Write("what Index:");
                var sourceLine = Console.ReadLine();
                if (!int.TryParse(sourceLine, out var index) || !statments.ContainsKey(index))
                {
                    PrintStatmentOptions();
                    continue;
                }

                return index;
            }
        }

        private static (Assembly assembly, List<(int, string)> statmentIndexes) Build(string sourceCode)
        {
            var (generatedCode, indexesOfStatments) = SyntaxHelper.InsertBreakpoints(sourceCode);
            //   File.WriteAllText(GeneratedPath, generatedCode, Encoding.UTF8);

            var syntaxTree = CSharpSyntaxTree.ParseText(generatedCode);
            var assemblyName = Path.GetRandomFileName();


            var compilation = CSharpCompilation.Create(
                assemblyName,
                syntaxTrees: new[] { syntaxTree },
                references: References(),
                options: new CSharpCompilationOptions(OutputKind.ConsoleApplication));

            Assembly assembly = null;
            using (var ms = new MemoryStream())
            {
                EmitResult result = compilation.Emit(ms);
                if (!result.Success)
                {
                    IEnumerable<Diagnostic> failures = result.Diagnostics.Where(diagnostic =>
                        diagnostic.IsWarningAsError ||
                        diagnostic.Severity == DiagnosticSeverity.Error);
                    foreach (Diagnostic diagnostic in failures)
                    {
                        Debugger.Break();
                    }
                }
                else
                {
                    ms.Seek(0, SeekOrigin.Begin);
                    assembly = Assembly.Load(ms.ToArray());
                }

                ms.Close();
            }
            return (assembly, indexesOfStatments);

        }
    }
    public static class SyntaxHelper
    {
        public static (string code, List<(int, string)> indexesOfStatments) InsertBreakpoints(string sourceCode)
        {
            var root = ParseText(sourceCode);

            var methods = root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>().ToArray();
            var output = "";

            var statements = DetectStatements(methods);

            var result = sourceCode;
            var linesOfSource = sourceCode.Split('\n');
            var indexesOfStatments = new List<(int, string)>();
            foreach (var (statement, variables) in statements.Reverse())
            {
                var (span, debugIndex) = GetSpan(statement);
                var gen =
                    $"DebugHelper.BreakPoint({span.Start}, {span.Length}{ToParamsArrayText(variables)});\r\n";
                result = result.Insert(debugIndex, gen);
                var lineOfIndex = 0;
                for (int i = 0; i < sourceCode.Length; i++)
                {
                    if (sourceCode[i] == '\n') lineOfIndex++;
                    if (i == span.Start) break;
                }
                indexesOfStatments.Add((span.Start, linesOfSource[lineOfIndex]));
            }

            output += result;


            return (output.Insert(root.Usings.FullSpan.End, "using DebuggerLib;\r\n"), indexesOfStatments.OrderBy(x => x.Item1).ToList());
        }

        public static CompilationUnitSyntax ParseText(string text)
        {
            var tree = CSharpSyntaxTree.ParseText(text);
            var diagnostics = tree.GetDiagnostics().ToArray();
            if (diagnostics.Length > 0) throw new FormatException(diagnostics[0].ToString());

            return tree.GetCompilationUnitRoot();
        }

        private static DebugStatement[] DetectStatements(MethodDeclarationSyntax[] methods)
        {
            var statements = new List<DebugStatement>();
            foreach (var method in methods)
            {
                DetectStatements(method.Body, statements, new List<(string, SyntaxNode)>());
            }

            return statements.ToArray();
        }

        static void DetectStatements(SyntaxNode node, List<DebugStatement> statements, List<(string name, SyntaxNode scope)> variables)
        {
            // Adds variables.
            if (node is VariableDeclarationSyntax varSyntax)
            {
                var varNames = varSyntax.Variables.Select(v => v.Identifier.ValueText).ToArray();
                var scope = ((node.Parent is LocalDeclarationStatementSyntax) ? node.Parent : node)
                    .Ancestors()
                    .First(n => n is StatementSyntax);

                variables.AddRange(varNames.Select(v => (v, scope)));
            }

            // Maps variables to the statement.
            if ((node is StatementSyntax statement) &&
                !(node is BlockSyntax) &&
                !(node is BreakStatementSyntax))
                statements.Add((statement, variables.Select(v => v.name).ToArray()));

            // Recursively.
            foreach (var child in node.ChildNodes())
                DetectStatements(child, statements, variables);

            // Maps variables to the last line of the block.
            if (node is BlockSyntax block)
                statements.Add((block, variables.Select(v => v.name).ToArray()));

            // Clears variables out of the scope.
            if (node is StatementSyntax)
                for (var i = variables.Count - 1; i >= 0; i--)
                    if (variables[i].scope == node)
                        variables.RemoveAt(i);
                    else
                        break;
        }

        static (TextSpan, int) GetSpan(StatementSyntax statement)
        {
            switch (statement)
            {
                case ForStatementSyntax f:
                    var span = new TextSpan(f.ForKeyword.Span.Start, f.CloseParenToken.Span.End - f.ForKeyword.Span.Start);
                    return (span, statement.FullSpan.Start);
                case BlockSyntax b:
                    return (b.CloseBraceToken.Span, b.CloseBraceToken.FullSpan.Start);
                default:
                    return (statement.Span, statement.FullSpan.Start);
            }
        }

        static string ToParamsArrayText(string[] variables) =>
            string.Concat(variables.Select(v => $", new Var(\"{v}\", {v})"));

        static string ToDictionaryText(string[] variables) =>
            $"new Dictionary<string, object> {{ {string.Join(", ", variables.Select(v => $"{{ \"{v}\", {v} }}"))} }}";
    }

}