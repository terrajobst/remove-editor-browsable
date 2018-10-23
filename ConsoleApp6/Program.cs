using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ConsoleApp6
{
    static class Program
    {
        static void Main(string[] args)
        {
            var directory = @"P:\oss\standard\netstandard\ref";
            var files = Directory.EnumerateFiles(directory, "*.cs");

            var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp7_3);
            var syntaxTrees = files.Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f), path: f, options: parseOptions));
            var compilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true);
            var compilation = CSharpCompilation.Create("netstandard", syntaxTrees, options: compilationOptions);

            var hasIssues = false;

            foreach (var diagnostic in compilation.GetDiagnostics())
            {
                if (diagnostic.Id == "CS3021" || diagnostic.Id == "CS0809" || diagnostic.Id == "CS0618")
                    continue;

                hasIssues = true;
                Console.WriteLine(diagnostic);
            }

            if (hasIssues)
                return;

            var symbols = new List<ISymbol>();
            GetSymbols(compilation.Assembly.GlobalNamespace, NetFramework461.MissingApis, symbols);

            var foundSymbolIds = new HashSet<string>(symbols.Select(s => s.GetDocumentationCommentId()));

            foreach (var netFxApi in NetFramework461.MissingApis)
            {
                if (!foundSymbolIds.Contains(netFxApi))
                    Console.WriteLine($"Couldn't find {netFxApi}");
            }

            var locations = new List<Location>();
            var processedSymbols = new HashSet<ISymbol>();

            foreach (var symbol in symbols)
            {
                foreach (var attribute in symbol.GetAttributes())
                {
                    if (attribute.AttributeClass.MetadataName == "EditorBrowsableAttribute")
                    {
                        var node = attribute.ApplicationSyntaxReference.GetSyntax().FirstAncestorOrSelf<AttributeListSyntax>();
                        Debug.Assert(node != null);
                        var location = node.GetLocation();
                        locations.Add(location);
                        processedSymbols.Add(symbol);
                    }
                }
            }

            Console.WriteLine($"Found: {processedSymbols.Count}, Missing: {symbols.Where(s => !processedSymbols.Contains(s)).Count()}");

            foreach (var symbol in symbols.Where(s => !processedSymbols.Contains(s)))
                Console.WriteLine($"No [EditorBrowsable] on {symbol.ToDisplayString()}");

            foreach (var group in locations.GroupBy(l => l.SourceTree))
            {
                var tree = group.Key;
                var text = tree.GetText();

                foreach (var location in group.OrderByDescending(l => l.SourceSpan.Start))
                {
                    var line = text.Lines.GetLineFromPosition(location.SourceSpan.Start);
                    text = text.Replace(line.SpanIncludingLineBreak, string.Empty);
                }

                File.WriteAllText(group.Key.FilePath, text.ToString());
            }
        }

        static void GetSymbols(INamespaceOrTypeSymbol container, ISet<string> docIds, List<ISymbol> receiver)
        {
            foreach (var member in container.GetMembers())
            {
                var id = member.GetDocumentationCommentId();
                if (docIds.Contains(id))
                    receiver.Add(member);

                if (member is INamespaceOrTypeSymbol nestedContainer)
                    GetSymbols(nestedContainer, docIds, receiver);
            }
        }
    }
}
