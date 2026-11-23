using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UtilityTools.AIConsole;

namespace UtilityTools.Tests
{
    public class ScreenshotArgsTests
    {
        [Test]
        public void ViewFileAndSizeAreOptionalInThatOrder()
        {
            var plain = ScreenshotVerb.Parse("game", out _).Value;
            Assert.AreEqual("game", plain.View);
            Assert.IsNull(plain.File);
            Assert.IsFalse(plain.HasSize);

            var full = ScreenshotVerb.Parse("scene shots/a.png 640 360", out _).Value;
            Assert.AreEqual("shots/a.png", full.File);
            Assert.AreEqual(640, full.Width);
            Assert.AreEqual(360, full.Height);

            var sizeOnly = ScreenshotVerb.Parse("game 1080 1920", out _).Value;
            Assert.IsNull(sizeOnly.File);
            Assert.AreEqual(1920, sizeOnly.Height);
        }

        [Test]
        public void BadArgumentsAreRejectedWithAReason()
        {
            Assert.IsNull(ScreenshotVerb.Parse("", out string e1));
            Assert.IsNull(ScreenshotVerb.Parse("sideways", out _));
            Assert.IsNull(ScreenshotVerb.Parse("game 640", out string e2), "a size needs both numbers");
            Assert.IsNull(ScreenshotVerb.Parse("game 0 100", out _));
            Assert.IsNull(ScreenshotVerb.Parse("game 99999 100", out _));
            StringAssert.StartsWith("usage:", e1);
            StringAssert.Contains("size", e2);
        }
    }

    public class RunVerbTests
    {
        private class Samples
        {
            [AgentCommand("tests.ok", "tests.ok [name]")] public static string Greet(string who) => "hi " + who;
            [AgentCommand("tests.plain")] public static void Plain() { }
            [AgentCommand("tests.instance")] public string Instance() => "";
            [AgentCommand("tests.ints")] public static void Ints(int n) { }
            [AgentCommand("two words")] public static void Spaced() { }
        }

        private static MethodInfo[] SampleMethods =>
            typeof(Samples).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        [Test]
        public void DiscoverySortsByIdAndExplainsWhatCantRun()
        {
            var entries = RunVerb.Discover(SampleMethods);
            var byId = entries.ToDictionary(e => e.Id);

            CollectionAssert.IsOrdered(entries.Select(e => e.Id).ToList());
            Assert.IsNull(byId["tests.ok"].Problem);
            Assert.AreEqual("tests.ok [name]", byId["tests.ok"].Usage);
            Assert.IsNull(byId["tests.plain"].Problem);
            Assert.AreEqual("tests.plain", byId["tests.plain"].Usage, "usage defaults to the id");
            StringAssert.Contains("static", byId["tests.instance"].Problem);
            StringAssert.Contains("one string", byId["tests.ints"].Problem);
            StringAssert.Contains("one word", byId["two words"].Problem);
        }
    }

    public class InspectPathTests
    {
        private GameObject _root;

        [SetUp]
        public void BuildHierarchy()
        {
            // Hidden, never-saved objects leave the user's open scene untouched.
            _root = Hidden("Main Camera", null);
            var hull = Hidden("Hull", _root.transform);
            Hidden("Deck 2", hull.transform);
        }

        [TearDown]
        public void DestroyHierarchy() => Object.DestroyImmediate(_root);

        private static GameObject Hidden(string name, Transform parent)
        {
            var go = UnityEditor.EditorUtility.CreateGameObjectWithHideFlags(name, HideFlags.HideAndDontSave);
            if (parent != null) go.transform.SetParent(parent);
            return go;
        }

        [Test]
        public void FindsNestedObjectsWhoseNamesHaveSpaces()
        {
            var roots = new[] { _root };
            Assert.AreEqual("Deck 2", InspectVerb.FindByPath(roots, "Main Camera/Hull/Deck 2").name);
            Assert.AreEqual("Hull", InspectVerb.FindByPath(roots, "/Main Camera/Hull/").name);
            Assert.IsNull(InspectVerb.FindByPath(roots, "Main Camera/Nope"));
        }

        [Test]
        public void ATrailingWordIsAComponentWhenThePathDoesNotExist()
        {
            var roots = new[] { _root };

            Assert.IsTrue(InspectVerb.TryResolve(roots, "Main Camera/Hull", out var whole, out string none));
            Assert.AreEqual("Hull", whole.name);
            Assert.IsNull(none);

            Assert.IsTrue(InspectVerb.TryResolve(roots, "Main Camera Transform", out var root, out string component));
            Assert.AreEqual("Main Camera", root.name);
            Assert.AreEqual("Transform", component);

            Assert.IsFalse(InspectVerb.TryResolve(roots, "Nothing Here", out _, out _));
        }
    }

    public class AgentOutputTests
    {
        [Test]
        public void LongOrMultiLineResultsGoToAFile()
        {
            Assert.IsFalse(AgentQueue.NeedsOutputFile("compiled (3.2s)"));
            Assert.IsTrue(AgentQueue.NeedsOutputFile("line one\nline two"));
            Assert.IsTrue(AgentQueue.NeedsOutputFile(new string('x', 400)));
        }

        [Test]
        public void SummaryIsTheFirstLineClipped()
        {
            Assert.AreEqual("GameObject Hull (active)", AgentQueue.Summary("GameObject Hull (active)\r\n- Transform: …"));
            StringAssert.EndsWith("…", AgentQueue.Summary(new string('x', 500)));
        }

        [Test]
        public void ReadOnlyExtensionVerbsRunDuringSomeoneElsesLease()
        {
            Assert.IsFalse(AgentLease.NeedsLease("screenshot"));
            Assert.IsFalse(AgentLease.NeedsLease("inspect"));
            Assert.IsTrue(AgentLease.NeedsLease("run"));
            Assert.IsTrue(AgentLease.NeedsLease("scene"));
            Assert.IsTrue(AgentLease.NeedsLease("packages"));
        }
    }
}
