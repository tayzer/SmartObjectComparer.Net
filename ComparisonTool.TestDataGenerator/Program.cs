using System.Xml.Linq;

namespace ComparisonTool.TestDataGenerator
{
    class Program
    {
        static readonly string BaseDir = Path.Combine("TestFiles", "CategorizationCases");
        static readonly string TemplatePath = Path.Combine("..", "CollectionOrdering", "Expecteds", "OrderTest.xml");
        static readonly int FileCount = 5;

        static void Main(string[] args)
        {
            // Find the solution root (assume running from bin/Debug/netX.X or project root)
            string current = AppDomain.CurrentDomain.BaseDirectory;
            string solutionRoot = current;
            int maxUp = 6; // go up at most 6 levels
            for (int i = 0; i < maxUp; i++)
            {
                if (File.Exists(Path.Combine(solutionRoot, "ComparisonTool.sln")))
                    break;
                solutionRoot = Path.GetFullPath(Path.Combine(solutionRoot, ".."));
            }
            string domainTestFiles = Path.Combine(solutionRoot, "ComparisonTool.Domain", "TestFiles");
            string baseDir = Path.Combine(domainTestFiles, "CategorizationCases");
            string templateFullPath = Path.Combine(solutionRoot, "ComparisonTool.Domain", "TestFiles", "CollectionOrdering", "Expecteds", "OrderTest.xml");
            if (!File.Exists(templateFullPath))
            {
                Console.WriteLine($"Template not found: {templateFullPath}");
                return;
            }
            XDocument template = XDocument.Load(templateFullPath);

            GenerateCategory(
                baseDir, "MissingRelatedItems", template, (doc, i) => RemoveElement(doc, "RelatedItems"),
                "Actual files are missing the <RelatedItems> block.");
            GenerateCategory(
                baseDir, "CollectionOrderIssues", template, (doc, i) => ShuffleResultsOrder(doc, i),
                "Actual files have <Results> and <RelatedItems> in a different order.");
            GenerateCategory(
                baseDir, "ValueChanges", template, (doc, i) => ChangeResultValue(doc, i),
                "Actual files have value changes in <Status>, <Score>, or <ItemName>.");
            GenerateCategory(
                baseDir, "ExtraElements", template, (doc, i) => AddExtraElement(doc, i),
                "Actual files have an extra <Result> or <Item> element.");
            GenerateCategory(
                baseDir, "NullVsValue", template, (doc, i) => RemoveDescription(doc, i),
                "Actual files are missing <Description> or have it empty.");
            GenerateCategory(
                baseDir, "Mixed", template, (doc, i) => { RemoveElement(doc, "RelatedItems"); ChangeResultValue(doc, i); },
                "Actual files have both missing <RelatedItems> and value changes.");

            Console.WriteLine("Test files generated in: " + baseDir);
        }

        static void GenerateCategory(string baseDir, string category, XDocument template, Action<XDocument, int> actualEdit, string description)
        {
            string catDir = Path.Combine(baseDir, category);
            string expecteds = Path.Combine(catDir, "Expecteds");
            string actuals = Path.Combine(catDir, "Actuals");
            Directory.CreateDirectory(expecteds);
            Directory.CreateDirectory(actuals);
            File.WriteAllText(Path.Combine(catDir, "README.md"), $"# {category}\n\n{description}\n\n- {FileCount} test pairs generated.\n");
            for (int i = 1; i <= FileCount; i++)
            {
                // Expecteds: always the template
                var expected = new XDocument(template);
                expected.Save(Path.Combine(expecteds, $"{i}.xml"));
                // Actuals: apply the category-specific edit
                var actual = new XDocument(template);
                actualEdit(actual, i);
                actual.Save(Path.Combine(actuals, $"{i}.xml"));
            }
        }

        static void RemoveElement(XDocument doc, string elementName)
        {
            var ns = doc.Root.Descendants().First().Name.Namespace;
            var el = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == elementName);
            el?.Remove();
        }

        static void ShuffleResultsOrder(XDocument doc, int i)
        {
            var ns = doc.Root.Descendants().First().Name.Namespace;
            var results = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Results");
            if (results != null)
            {
                var children = results.Elements().ToList();
                // Simple shuffle: rotate by i
                var shuffled = children.Skip(i % children.Count).Concat(children.Take(i % children.Count)).ToList();
                results.RemoveNodes();
                foreach (var c in shuffled) results.Add(c);
            }
            var related = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "RelatedItems");
            if (related != null)
            {
                var children = related.Elements().ToList();
                var shuffled = children.AsEnumerable().Reverse().ToList(); // Just reverse for variety
                related.RemoveNodes();
                foreach (var c in shuffled) related.Add(c);
            }
        }

        static void ChangeResultValue(XDocument doc, int i)
        {
            var ns = doc.Root.Descendants().First().Name.Namespace;
            var results = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Results");
            if (results != null)
            {
                var resultList = results.Elements().ToList();
                if (resultList.Count > 0)
                {
                    var result = resultList[i % resultList.Count];
                    var status = result.Descendants().FirstOrDefault(e => e.Name.LocalName == "Status");
                    if (status != null) status.Value = (status.Value == "Success" ? "Failure" : "Success");
                    var score = result.Descendants().FirstOrDefault(e => e.Name.LocalName == "Score");
                    if (score != null) score.Value = (double.Parse(score.Value) + i).ToString("F1");
                }
            }
            var related = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "RelatedItems");
            if (related != null)
            {
                var items = related.Elements().ToList();
                if (items.Count > 0)
                {
                    var item = items[i % items.Count];
                    var name = item.Descendants().FirstOrDefault(e => e.Name.LocalName == "ItemName");
                    if (name != null) name.Value += " (Changed)";
                }
            }
        }

        static void AddExtraElement(XDocument doc, int i)
        {
            var ns = doc.Root.Descendants().First().Name.Namespace;
            var results = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Results");
            if (results != null)
            {
                var first = results.Elements().FirstOrDefault();
                if (first != null)
                {
                    var extra = new XElement(first);
                    extra.Element(first.Elements().First().Name).Value += "_Extra";
                    results.Add(extra);
                }
            }
            var related = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "RelatedItems");
            if (related != null)
            {
                var first = related.Elements().FirstOrDefault();
                if (first != null)
                {
                    var extra = new XElement(first);
                    extra.Element(first.Elements().First().Name).Value += "_Extra";
                    related.Add(extra);
                }
            }
        }

        static void RemoveDescription(XDocument doc, int i)
        {
            var results = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Results");
            if (results != null)
            {
                var resultList = results.Elements().ToList();
                if (resultList.Count > 0)
                {
                    var result = resultList[i % resultList.Count];
                    var desc = result.Descendants().FirstOrDefault(e => e.Name.LocalName == "Description");
                    desc?.Remove();
                }
            }
        }
    }
}