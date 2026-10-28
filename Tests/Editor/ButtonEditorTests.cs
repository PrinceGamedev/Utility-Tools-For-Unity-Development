using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace UtilityTools.Tests
{
    public class ButtonEditorTests
    {
        // ---------- Fixtures ----------
        private class BaseTarget
        {
            [Button] private void PrivateBase() { }
            [Button] protected virtual void Virt() { }
            private void NotAButton() { }
        }

        private class DerivedTarget : BaseTarget
        {
            [Button] private void Zeta() { }   // declared before Alpha on purpose
            [Button] private void Alpha() { }
            protected override void Virt() { } // inherits [Button] from the base declaration
            [Button] private static void StaticOne() { }
        }

        // ---------- BuildTypeCache ----------
        [Test]
        public void BuildTypeCache_BaseFirst_ThenDeclarationOrder()
        {
            var names = ButtonEditorBase.BuildTypeCache(typeof(DerivedTarget)).Select(m => m.Method.Name).ToArray();
            CollectionAssert.AreEqual(new[] { "PrivateBase", "Zeta", "Alpha", "Virt", "StaticOne" }, names);
        }

        [Test]
        public void BuildTypeCache_OverrideAppearsOnce_AsMostDerived()
        {
            var virt = ButtonEditorBase.BuildTypeCache(typeof(DerivedTarget)).Where(m => m.Method.Name == "Virt").ToArray();
            Assert.AreEqual(1, virt.Length);
            Assert.AreEqual(typeof(DerivedTarget), virt[0].Method.DeclaringType);
        }

        [Test]
        public void BuildTypeCache_FlagsStaticAndReturnKinds()
        {
            var metas = ButtonEditorBase.BuildTypeCache(typeof(DerivedTarget));
            Assert.IsTrue(metas.Single(m => m.Method.Name == "StaticOne").IsStatic);
            Assert.IsFalse(metas.Single(m => m.Method.Name == "Alpha").ReturnsValue);
        }

        // ---------- Helpers ----------
        [TestCase("#c0392b", true)]
        [TestCase("e74c3c", true)]
        [TestCase("not a color", false)]
        [TestCase(null, false)]
        public void TryParseColor(string hex, bool expected)
        {
            Assert.AreEqual(expected, ButtonEditorBase.TryParseColor(hex, out _));
        }

        [TestCase("123|Type|Method", true, 123)]
        [TestCase("-45|Type|Method", true, -45)]
        [TestCase("abc|Type", false, 0)]
        [TestCase("nobar", false, 0)]
        public void TryGetOwnerId(string key, bool expected, int expectedId)
        {
            Assert.AreEqual(expected, ButtonEditorBase.TryGetOwnerId(key, out int id));
            if (expected) Assert.AreEqual(expectedId, id);
        }

        // ---------- EditorCoroutineDriver ----------
        private static IEnumerator Outer(System.Collections.Generic.List<string> log)
        {
            log.Add("outer-start");
            yield return Inner(log);
            log.Add("outer-end");
        }

        private static IEnumerator Inner(System.Collections.Generic.List<string> log)
        {
            log.Add("inner-1");
            yield return null;
            log.Add("inner-2");
        }

        [Test]
        public void CoroutineDriver_RunsNestedEnumeratorsToCompletion()
        {
            var log = new System.Collections.Generic.List<string>();
            var routine = EditorCoroutineDriver.Start(Outer(log));

            int ticks = 0;
            while (EditorCoroutineDriver.Step(routine) && ticks < 100) ticks++;

            CollectionAssert.AreEqual(new[] { "outer-start", "inner-1", "inner-2", "outer-end" }, log);
        }

        [Test]
        public void CoroutineDriver_WaitsOnCustomYieldInstruction()
        {
            int ticks = 0;
            bool done = false;

            IEnumerator Routine()
            {
                yield return new WaitUntil(() => ticks >= 3);
                done = true;
            }

            var routine = EditorCoroutineDriver.Start(Routine());
            while (EditorCoroutineDriver.Step(routine) && ticks < 100)
            {
                ticks++;
                if (ticks < 3) Assert.IsFalse(done);
            }

            Assert.IsTrue(done);
        }
    }
}
