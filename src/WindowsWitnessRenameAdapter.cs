using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Jarvis
{
    // This is a single witness operation, not an arbitrary filesystem capability.
    public sealed class WindowsWitnessRenameAdapter : IWitnessRenameAdapter
    {
        private const int MaximumBytes = 128 * 1024;
        private const uint GenericRead = 0x80000000, DeleteAccess = 0x00010000;
        private const uint OpenReparsePoint = 0x00200000, BackupSemantics = 0x02000000;
        private readonly string firstPath;
        private readonly string secondPath;
        private readonly string directory;

        public WindowsWitnessRenameAdapter(string firstPath, string secondPath)
        {
            this.firstPath = LocalTextPath(firstPath);
            this.secondPath = LocalTextPath(secondPath);
            directory = Path.GetDirectoryName(this.firstPath);
            if (Same(this.firstPath, this.secondPath) || !Same(directory, Path.GetDirectoryName(this.secondPath)))
                throw new InvalidOperationException("Les deux noms témoins doivent être distincts, dans le même dossier local.");
        }

        public RenameSnapshot Prepare(string source, string destination)
        {
            AuthorizePair(ref source, ref destination);
            using (var parents = new DirectoryLocks(directory))
            using (SafeFileHandle handle = OpenSource(source))
            using (var file = new FileStream(handle, FileAccess.Read))
            {
                RequireAbsent(destination);
                FileState state = ReadState(file, source);
                return new RenameSnapshot(source, destination, state.Version);
            }
        }

        public RenameEvidence Execute(RenameSnapshot snapshot)
        {
            if (snapshot == null) throw new InvalidOperationException("Aucun renommage préparé.");
            string source = snapshot.Source;
            string destination = snapshot.Destination;
            AuthorizePair(ref source, ref destination);
            using (var parents = new DirectoryLocks(directory))
            using (SafeFileHandle handle = OpenSource(source))
            using (var file = new FileStream(handle, FileAccess.Read))
            {
                RequireAbsent(destination);
                FileState before = ReadState(file, source);
                if (!String.Equals(before.Version, snapshot.Version, StringComparison.Ordinal))
                    throw new InvalidOperationException("Le document témoin a changé depuis sa préparation ; un nouvel accord est requis.");

                // The same locked file handle is verified and renamed. Never resolve the source again.
                // Parent handles prevent directory replacement or conversion to a junction meanwhile.
                Rename(handle, destination);

                // Keep the handle locked while observing its new name, identity and unchanged content.
                FileState after = ReadState(file, destination);
                bool verified = !Exists(source) && String.Equals(before.Version, after.Version, StringComparison.Ordinal);
                return new RenameEvidence(source, destination, verified, verified
                    ? "Renommage Windows vérifié : ancienne cible absente, même fichier à la nouvelle cible. SHA-256 : " + after.Hash + "."
                    : "Le renommage a été demandé, mais son état final n’est pas entièrement vérifié.");
            }
        }

        private void AuthorizePair(ref string source, ref string destination)
        {
            source = LocalTextPath(source);
            destination = LocalTextPath(destination);
            if (!((Same(source, firstPath) && Same(destination, secondPath)) ||
                (Same(source, secondPath) && Same(destination, firstPath))))
                throw new InvalidOperationException("Seul le renommage aller-retour entre les deux témoins est autorisé.");
        }

        private static string LocalTextPath(string path)
        {
            if (String.IsNullOrWhiteSpace(path) || path.Length < 3 || !Char.IsLetter(path[0]) ||
                path[1] != ':' || (path[2] != '\\' && path[2] != '/') || path.IndexOf(':', 2) >= 0)
                throw new InvalidOperationException("Un chemin absolu sur un disque local est requis.");
            string full = Path.GetFullPath(path);
            if (!String.Equals(Path.GetExtension(full), ".txt", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Seuls les témoins texte .txt sont autorisés.");
            uint driveType = GetDriveType(Path.GetPathRoot(full));
            if (driveType != 2 && driveType != 3 && driveType != 6)
                throw new InvalidOperationException("Le témoin doit résider sur un disque local.");
            foreach (string part in full.Substring(3).Split('\\'))
                if (part.EndsWith(".", StringComparison.Ordinal) || part.EndsWith(" ", StringComparison.Ordinal))
                    throw new InvalidOperationException("Les noms avec un suffixe ambigu ne sont pas autorisés.");
            return full;
        }

        private static SafeFileHandle OpenSource(string path)
        {
            // No write/delete sharing: reject existing writers and block later changes/replacement.
            SafeFileHandle handle = CreateFile(path, GenericRead | DeleteAccess, 1, IntPtr.Zero, 3,
                OpenReparsePoint, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error();
                handle.Dispose();
                throw WindowsError("Impossible de verrouiller le document témoin.", error);
            }
            return handle;
        }

        private static FileState ReadState(FileStream file, string expectedPath)
        {
            FileInformation info = Information(file.SafeFileHandle);
            if ((info.Attributes & (uint)(FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.ReadOnly)) != 0 ||
                info.NumberOfLinks != 1)
                throw new IOException("Le témoin doit être un fichier ordinaire modifiable, sans lien ni redirection.");
            RequireFinalPath(file.SafeFileHandle, expectedPath);
            if (info.SizeHigh != 0 || info.SizeLow > MaximumBytes)
                throw new IOException("Le document témoin est limité à 128 Kio.");
            file.Position = 0;
            byte[] bytes = new byte[(int)info.SizeLow];
            int offset = 0;
            while (offset < bytes.Length)
            {
                int count = file.Read(bytes, offset, bytes.Length - offset);
                if (count == 0) throw new IOException("Lecture du témoin incomplète.");
                offset += count;
            }
            if (file.ReadByte() != -1) throw new IOException("La taille du témoin a changé.");
            string text = new UTF8Encoding(false, true).GetString(bytes);
            if (text.IndexOf('\0') >= 0) throw new IOException("Le témoin doit contenir du texte UTF-8 sans caractère nul.");
            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
            string identity = Identity(info);
            if (!String.Equals(identity, Identity(Information(file.SafeFileHandle)), StringComparison.Ordinal))
                throw new IOException("Les métadonnées du témoin ont changé pendant la vérification.");
            return new FileState(identity + ":" + hash, hash);
        }

        private static string Identity(FileInformation info)
        {
            // The file index distinguishes replacement even when content and timestamps match.
            return info.VolumeSerial.ToString("X8") + info.IndexHigh.ToString("X8") + info.IndexLow.ToString("X8") + ":" +
                info.CreationHigh.ToString("X8") + info.CreationLow.ToString("X8") + ":" +
                info.WriteHigh.ToString("X8") + info.WriteLow.ToString("X8") + ":" +
                info.SizeHigh.ToString("X8") + info.SizeLow.ToString("X8") + ":" + info.Attributes.ToString("X8") + ":" +
                info.NumberOfLinks.ToString("X8");
        }

        private static void Rename(SafeFileHandle file, string destination)
        {
            byte[] bytes = Encoding.Unicode.GetBytes(destination);
            int lengthOffset = 2 * IntPtr.Size;
            int nameOffset = lengthOffset + 4;
            int size = nameOffset + bytes.Length + 2;
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                // FILE_RENAME_INFO has pointer-sized padding after ReplaceIfExists.
                Marshal.Copy(new byte[size], 0, buffer, size);
                Marshal.WriteInt32(buffer, lengthOffset, bytes.Length);
                Marshal.Copy(bytes, 0, IntPtr.Add(buffer, nameOffset), bytes.Length);
                // ReplaceIfExists remains FALSE, so even a destination created now is never overwritten.
                if (!SetFileInformationByHandle(file, 3, buffer, (uint)size))
                    throw WindowsError("Le renommage Windows a été refusé.", Marshal.GetLastWin32Error());
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        private static void RequireAbsent(string path)
        {
            if (Exists(path)) throw new IOException("Le nom de destination est déjà occupé ; aucun écrasement n’est autorisé.");
        }

        private static bool Exists(string path)
        {
            uint attributes = GetFileAttributes(path);
            if (attributes != UInt32.MaxValue) return true;
            int error = Marshal.GetLastWin32Error();
            if (error == 2 || error == 3) return false;
            throw WindowsError("Impossible de vérifier l’état du chemin témoin.", error);
        }

        private static FileInformation Information(SafeFileHandle handle)
        {
            FileInformation info;
            if (!GetFileInformationByHandle(handle, out info))
                throw WindowsError("Impossible de vérifier l’identité Windows du témoin.", Marshal.GetLastWin32Error());
            return info;
        }

        private static void RequireFinalPath(SafeFileHandle handle, string expected)
        {
            var resolved = new StringBuilder(32768);
            uint length = GetFinalPathNameByHandle(handle, resolved, (uint)resolved.Capacity, 0);
            if (length == 0 || length >= resolved.Capacity || !Same(resolved.ToString(), @"\\?\" + expected))
                throw new IOException("La cible Windows réelle diffère du chemin témoin autorisé.");
        }

        private static bool Same(string left, string right) { return String.Equals(left, right, StringComparison.OrdinalIgnoreCase); }
        private static IOException WindowsError(string message, int error) { return new IOException(message, new Win32Exception(error)); }

        private sealed class FileState
        {
            internal readonly string Version, Hash;
            internal FileState(string version, string hash) { Version = version; Hash = hash; }
        }

        private sealed class DirectoryLocks : IDisposable
        {
            private readonly List<SafeFileHandle> handles = new List<SafeFileHandle>();
            internal DirectoryLocks(string path)
            {
                try
                {
                    string root = Path.GetPathRoot(path);
                    Lock(root);
                    string current = root;
                    foreach (string part in path.Substring(root.Length).Split('\\'))
                    {
                        if (part.Length == 0) continue;
                        current = Path.Combine(current, part);
                        Lock(current);
                    }
                }
                catch { Dispose(); throw; }
            }
            private void Lock(string path)
            {
                // Holding every ancestor without write/delete sharing also closes parent-path races.
                SafeFileHandle handle = CreateFile(path, 0x80, 1, IntPtr.Zero, 3,
                    BackupSemantics | OpenReparsePoint, IntPtr.Zero);
                if (handle.IsInvalid)
                {
                    int error = Marshal.GetLastWin32Error();
                    handle.Dispose();
                    throw WindowsError("Impossible de verrouiller le dossier témoin.", error);
                }
                handles.Add(handle);
                FileInformation info = Information(handle);
                if ((info.Attributes & (uint)FileAttributes.ReparsePoint) != 0 ||
                    (info.Attributes & (uint)FileAttributes.Directory) == 0)
                    throw new IOException("Les jonctions et liens dans le chemin témoin ne sont pas autorisés.");
                RequireFinalPath(handle, path);
            }
            public void Dispose()
            {
                for (int index = handles.Count - 1; index >= 0; index--) handles[index].Dispose();
                handles.Clear();
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FileInformation
        {
            internal uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh,
                VolumeSerial, SizeHigh, SizeLow, NumberOfLinks, IndexHigh, IndexLow;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security,
            uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern uint GetDriveType(string root);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFileAttributes(string path);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, uint length, uint flags);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation information);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetFileInformationByHandle(SafeFileHandle file, int informationClass, IntPtr information, uint size);
    }
}
