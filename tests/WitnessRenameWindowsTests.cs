using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Jarvis.Tests
{
    internal static class WindowsWitnessRenameTests
    {
        public static void Run()
        {
            string folder = Path.Combine(Path.GetTempPath(), "jarvis-rename-" + Guid.NewGuid());
            Directory.CreateDirectory(folder);
            string first = Path.Combine(folder, "bonjour.txt");
            string second = Path.Combine(folder, "bonjour-renomme.txt");
            string other = Path.Combine(folder, "autre.txt");
            const string content = "Preuve Windows réelle : été, café.\r\nTicket #3.";
            var adapter = new WindowsWitnessRenameAdapter(first, second);
            try
            {
                File.WriteAllText(first, content);
                var forward = adapter.Execute(adapter.Prepare(first, second));
                TestRunner.Check(forward.Verified && forward.Source == first && forward.Destination == second &&
                    !File.Exists(first) && File.ReadAllText(second) == content, "Real rename has matching observable evidence");
                var reverse = adapter.Execute(adapter.Prepare(second, first));
                TestRunner.Check(reverse.Verified && !File.Exists(second) && File.ReadAllText(first) == content,
                    "Real reverse rename restores the witness with identical content");

                RenameSnapshot stale = adapter.Prepare(first, second);
                DateTime originalWrite = File.GetLastWriteTimeUtc(first);
                string changedContent = content.Replace("Ticket #3.", "Ticket #4.");
                File.WriteAllText(first, changedContent);
                File.SetLastWriteTimeUtc(first, originalWrite);
                ExpectRefused(delegate { adapter.Execute(stale); }, "Content changes invalidate the prepared rename");
                TestRunner.Check(File.ReadAllText(first) == changedContent && !File.Exists(second),
                    "A stale content snapshot has no rename effect");

                File.WriteAllText(first, content);
                stale = adapter.Prepare(first, second);
                DateTime originalCreation = File.GetCreationTimeUtc(first);
                originalWrite = File.GetLastWriteTimeUtc(first);
                // Retain the original file under another name, preventing Windows file-ID reuse.
                File.Move(first, other);
                File.WriteAllText(first, content);
                File.SetCreationTimeUtc(first, originalCreation);
                File.SetLastWriteTimeUtc(first, originalWrite);
                ExpectRefused(delegate { adapter.Execute(stale); }, "Replacing a file invalidates its identity despite identical text and timestamps");
                TestRunner.Check(File.ReadAllText(first) == content && File.ReadAllText(other) == content && !File.Exists(second),
                    "Replacement source remains untouched");

                stale = adapter.Prepare(first, second);
                File.WriteAllText(second, "Destination occupée");
                ExpectRefused(delegate { adapter.Prepare(first, second); }, "An occupied destination cannot be prepared");
                ExpectRefused(delegate { adapter.Execute(stale); }, "A destination occupied after preparation cannot be overwritten");
                TestRunner.Check(File.ReadAllText(first) == content && File.ReadAllText(second) == "Destination occupée",
                    "Neither file changes on an occupied destination");
                File.Delete(second);

                stale = adapter.Prepare(first, second);
                string[] forbidden = { other, first + ":secret", @"\\server\share\bonjour.txt", "bonjour.txt", first + ".exe" };
                foreach (string path in forbidden)
                {
                    ExpectRefused(delegate { adapter.Prepare(first, path); }, "Destination outside the two witnesses is rejected");
                    ExpectRefused(delegate { adapter.Prepare(path, second); }, "Source outside the two witnesses is rejected");
                    ExpectRefused(delegate { adapter.Execute(new RenameSnapshot(first, path, stale.Version)); },
                        "The external execution boundary rechecks its destination");
                    ExpectRefused(delegate { adapter.Execute(new RenameSnapshot(path, second, stale.Version)); },
                        "The external execution boundary rechecks its source");
                }
                ExpectRefused(delegate { adapter.Prepare(first, first); }, "A rename onto itself is rejected");
                ExpectRefused(delegate { adapter.Execute(null); }, "An absent snapshot cannot execute");
                TestRunner.Check(File.ReadAllText(first) == content && File.ReadAllText(other) == content && !File.Exists(second),
                    "Out-of-scope calls have no effects");

                File.SetAttributes(first, FileAttributes.ReadOnly);
                try { ExpectRefused(delegate { adapter.Prepare(first, second); }, "Read-only witnesses are rejected before confirmation"); }
                finally { File.SetAttributes(first, FileAttributes.Normal); }
                foreach (byte[] invalid in new[] { new byte[128 * 1024 + 1], new byte[] { 65, 0, 66 }, new byte[] { 0xff, 0xff } })
                {
                    File.WriteAllBytes(first, invalid);
                    ExpectRefused(delegate { adapter.Prepare(first, second); }, "Oversized or non-text witnesses are rejected");
                    TestRunner.Check(File.ReadAllBytes(first).Length == invalid.Length && !File.Exists(second),
                        "An invalid witness is never renamed");
                }
                File.WriteAllText(first, content);
                JunctionIsRejected(folder, content);
                Console.WriteLine("PASS Windows witness rename: real round trip, stale content/identity, occupied destination, scope and junction refusals");
            }
            finally
            {
                if (File.Exists(first)) File.SetAttributes(first, FileAttributes.Normal);
                Directory.Delete(folder, true);
            }
        }

        private static void ExpectRefused(Action action, string message)
        {
            bool refused = false;
            try { action(); }
            catch (InvalidOperationException) { refused = true; }
            catch (IOException) { refused = true; }
            catch (UnauthorizedAccessException) { refused = true; }
            catch (ArgumentException) { refused = true; }
            TestRunner.Check(refused, message);
        }

        private static void JunctionIsRejected(string folder, string content)
        {
            string real = Path.Combine(folder, "real");
            string link = Path.Combine(folder, "junction");
            Directory.CreateDirectory(real);
            Directory.CreateDirectory(link);
            File.WriteAllText(Path.Combine(real, "bonjour.txt"), content);
            try
            {
                string source = Path.Combine(link, "bonjour.txt");
                string destination = Path.Combine(link, "bonjour-renomme.txt");
                File.WriteAllText(source, content);
                var adapter = new WindowsWitnessRenameAdapter(source, destination);
                RenameSnapshot stale = adapter.Prepare(source, destination);
                File.Delete(source);
                byte[] substitute = Encoding.Unicode.GetBytes(@"\??\" + real);
                byte[] display = Encoding.Unicode.GetBytes(real);
                byte[] data = new byte[16 + substitute.Length + 2 + display.Length + 2];
                Array.Copy(BitConverter.GetBytes(0xA0000003u), 0, data, 0, 4);
                Array.Copy(BitConverter.GetBytes((ushort)(data.Length - 8)), 0, data, 4, 2);
                Array.Copy(BitConverter.GetBytes((ushort)substitute.Length), 0, data, 10, 2);
                Array.Copy(BitConverter.GetBytes((ushort)(substitute.Length + 2)), 0, data, 12, 2);
                Array.Copy(BitConverter.GetBytes((ushort)display.Length), 0, data, 14, 2);
                Array.Copy(substitute, 0, data, 16, substitute.Length);
                Array.Copy(display, 0, data, 16 + substitute.Length + 2, display.Length);
                using (SafeFileHandle handle = CreateFile(link, 0x40000000, 7, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero))
                {
                    uint returned;
                    TestRunner.Check(!handle.IsInvalid && DeviceIoControl(handle, 0x000900A4, data, (uint)data.Length,
                        IntPtr.Zero, 0, out returned, IntPtr.Zero), "The Windows test creates a real directory junction");
                }
                ExpectRefused(delegate { adapter.Prepare(source, destination); }, "A parent junction cannot redirect the witness scope");
                ExpectRefused(delegate { adapter.Execute(stale); }, "A parent turned into a junction after preparation is rejected");
                TestRunner.Check(File.ReadAllText(Path.Combine(real, "bonjour.txt")) == content &&
                    !File.Exists(Path.Combine(real, "bonjour-renomme.txt")), "The junction target remains unchanged");
            }
            finally
            {
                // Delete only the junction itself, never recurse through its target.
                Directory.Delete(link, false);
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security,
            uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeviceIoControl(SafeFileHandle file, uint code, byte[] input, uint inputSize,
            IntPtr output, uint outputSize, out uint returned, IntPtr overlapped);
    }
}
