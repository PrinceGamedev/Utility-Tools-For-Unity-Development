using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace UtilityTools.Tests
{
    /// <summary>Keeps package.json, UtilityToolsInfo and VERSIONING.md in sync (see VERSIONING.md).</summary>
    public class VersioningTests
    {
        private static readonly Regex SemVer = new Regex(@"^\d+\.\d+\.\d+$");

        [Test]
        public void AllVersionsAreSemVer()
        {
            StringAssert.IsMatch(SemVer.ToString(), UtilityToolsInfo.PackageVersion);
            foreach (var f in UtilityToolsInfo.AllFeatures)
                StringAssert.IsMatch(SemVer.ToString(), f.Value);
        }

        [Test]
        public void EveryFeatureConstantIsListed()
        {
            var constants = typeof(UtilityToolsInfo.Features)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral)
                .Select(f => (string)f.GetRawConstantValue())
                .ToList();
            Assert.AreEqual(constants.Count, UtilityToolsInfo.AllFeatures.Count,
                "A Features constant is missing from AllFeatures (or vice versa)");
        }

        [Test]
        public void PackageVersionMatches()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(UtilityToolsInfo).Assembly);
            if (info == null) Assert.Ignore("Package installed outside Packages/ (copied into Assets/).");
            Assert.AreEqual(info.version, UtilityToolsInfo.PackageVersion,
                "package.json version and UtilityToolsInfo.PackageVersion differ");
        }

        [Test]
        public void VersioningDocMatchesCode()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(UtilityToolsInfo).Assembly);
            if (info == null) Assert.Ignore("Package installed outside Packages/ (copied into Assets/).");
            string doc = File.ReadAllText(Path.Combine(info.resolvedPath, "VERSIONING.md"));

            foreach (var f in UtilityToolsInfo.AllFeatures)
            {
                var row = new Regex(@"^\|\s*" + Regex.Escape(f.Key) + @"\s*\|\s*(?<v>[\d.]+)\s*\|", RegexOptions.Multiline).Match(doc);
                Assert.IsTrue(row.Success, f.Key + " missing from the VERSIONING.md table");
                Assert.AreEqual(f.Value, row.Groups["v"].Value, f.Key + " version differs in VERSIONING.md");
            }
        }
    }
}
