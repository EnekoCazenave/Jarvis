using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;

namespace Jarvis
{
    // A controlled text reader makes the actual displayed content observable.
    // Launching the default shell association alone would not prove document opening.
    public sealed class WindowsDocumentAdapter : IDocumentAdapter, IDisposable
    {
        private const int MaximumBytes = 128 * 1024;
        private readonly DocumentPolicy policy;
        private readonly List<Form> readers = new List<Form>();
        public WindowsDocumentAdapter(DocumentPolicy policy) { this.policy = policy; }

        public DocumentEvidence Open(string path)
        {
            string target = policy.AuthorizePath(path);
            using (var file = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                // Check the opened handle, not just the pre-open path, against junctions/symlinks.
                var resolved = new StringBuilder(32768);
                uint length = GetFinalPathNameByHandle(file.SafeFileHandle, resolved, (uint)resolved.Capacity, 0);
                if (length == 0 || length >= resolved.Capacity ||
                    !String.Equals(resolved.ToString(), @"\\?\" + target, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("La cible réelle du fichier diffère du document témoin autorisé.");
                if (file.Length > MaximumBytes) throw new IOException("Le lecteur témoin est limité à 128 Kio.");
                string content;
                using (var reader = new StreamReader(file, new UTF8Encoding(false, true), true, 4096, true))
                    content = reader.ReadToEnd();
                if (content.IndexOf('\0') >= 0) throw new IOException("Ce fichier n’est pas un document texte pris en charge.");
                var window = new Form {
                    Text = Path.GetFileName(target) + " — Lecteur Jarvis", Width = 760, Height = 520,
                    StartPosition = FormStartPosition.CenterScreen, BackColor = Color.White
                };
                var heading = new Label { Text = target, Dock = DockStyle.Top, Height = 48, Padding = new Padding(14), AutoEllipsis = true };
                var text = new TextBox {
                    Multiline = true, ReadOnly = true, Text = content, Dock = DockStyle.Fill,
                    ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 12),
                    BackColor = Color.White, BorderStyle = BorderStyle.None, AccessibleName = "Contenu du document témoin"
                };
                window.Controls.Add(text);
                window.Controls.Add(heading);
                readers.Add(window);
                window.FormClosed += delegate { readers.Remove(window); window.Dispose(); };
                window.Show();
                window.Refresh();
                bool visible = IsWindowVisible(window.Handle) && IsWindowVisible(text.Handle) && text.Text == content;
                string hash;
                using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.Text))).Replace("-", "");
                return new DocumentEvidence(target, visible,
                    "Fichier lu et contenu affiché dans le lecteur Windows Jarvis. SHA-256 du texte affiché : " + hash + ".");
            }
        }

        public void Dispose()
        {
            foreach (Form reader in readers.ToArray()) reader.Close();
            readers.Clear();
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, uint length, uint flags);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr window);
    }
}
