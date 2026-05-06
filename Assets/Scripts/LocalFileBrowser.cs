using System;
using System.Runtime.InteropServices;
using System.IO;
using System.Linq;

namespace Zhouxiangyang
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public class OpenFileName
    {
        public int structSize = 0;
        public IntPtr dlgOwner = IntPtr.Zero;
        public IntPtr instance = IntPtr.Zero;
        public String filter = null;
        public String customFilter = null;
        public int maxCustFilter = 0;
        public int filterIndex = 0;
        public IntPtr file = IntPtr.Zero;
        public int maxFile = 0;
        public IntPtr fileTitle = IntPtr.Zero;
        public int maxFileTitle = 0;
        public String initialDir = null;
        public String title = null;
        public int flags = 0;
        public short fileOffset = 0;
        public short fileExtension = 0;
        public String defExt = null;
        public IntPtr custData = IntPtr.Zero;
        public IntPtr hook = IntPtr.Zero;
        public String templateName = null;
        public IntPtr reservedPtr = IntPtr.Zero;
        public int reservedInt = 0;
        public int flagsEx = 0;
    }

    public class LocalFileBrowser
    {
        [DllImport("Comdlg32.dll", SetLastError = true, ThrowOnUnmappableChar = true, CharSet = CharSet.Auto)]
        public static extern bool GetOpenFileName([In, Out] OpenFileName ofn);
        
        [DllImport("Comdlg32.dll", SetLastError = true, ThrowOnUnmappableChar = true, CharSet = CharSet.Auto)]
        public static extern bool GetSaveFileName([In, Out] OpenFileName ofn);

        private const int OFN_FILEMUSTEXIST = 0x00001000;
        private const int OFN_PATHMUSTEXIST = 0x00000800;
        private const int OFN_EXPLORER = 0x00080000;
        private const int OFN_NOCHANGEDIR = 0x00000008;
        private const int OFN_ALLOWMULTISELECT = 0x00000200;
        private const int OFN_OVERWRITEPROMPT = 0x00000002;

        public static string OpenFile()
        {
            var files = OpenFiles();
            if (files == null || files.Length == 0) return null;
            return files[0];
        }

        public static string OpenProjectFile()
        {
            return OpenSingleFile("OCX工程文件 (*.ocxproj)\0*.ocxproj\0所有文件 (*.*)\0*.*\0", "打开工程文件（*.ocxproj）");
        }

        public static string SaveProjectFile(string defaultFileName = "ocx_project.ocxproj")
        {
            OpenFileName ofn = new OpenFileName();
            ofn.structSize = Marshal.SizeOf(ofn);
            ofn.filter = "OCX工程文件 (*.ocxproj)\0*.ocxproj\0所有文件 (*.*)\0*.*\0";
            int bufferChars = 4096;
            int titleChars = 512;
            IntPtr fileBuf = IntPtr.Zero;
            IntPtr titleBuf = IntPtr.Zero;
            try
            {
                fileBuf = Marshal.AllocHGlobal(bufferChars * sizeof(char));
                titleBuf = Marshal.AllocHGlobal(titleChars * sizeof(char));
                ZeroMemory(fileBuf, bufferChars * sizeof(char));
                ZeroMemory(titleBuf, titleChars * sizeof(char));
                if (!string.IsNullOrWhiteSpace(defaultFileName)) WriteStringToBuffer(fileBuf, bufferChars, defaultFileName);
                ofn.file = fileBuf;
                ofn.maxFile = bufferChars;
                ofn.fileTitle = titleBuf;
                ofn.maxFileTitle = titleChars;
                ofn.title = "保存工程文件（*.ocxproj）";
                ofn.defExt = "ocxproj";
                ofn.flags = OFN_EXPLORER | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR | OFN_OVERWRITEPROMPT;

                string currentUnityDirectory = Environment.CurrentDirectory;
                bool success = GetSaveFileName(ofn);
                Environment.CurrentDirectory = currentUnityDirectory;

                if (!success) return null;
                string result = Marshal.PtrToStringAuto(fileBuf);
                return string.IsNullOrWhiteSpace(result) ? null : result;
            }
            finally
            {
                if (fileBuf != IntPtr.Zero) Marshal.FreeHGlobal(fileBuf);
                if (titleBuf != IntPtr.Zero) Marshal.FreeHGlobal(titleBuf);
            }
        }

        public static string[] OpenFiles()
        {
            OpenFileName ofn = new OpenFileName();
            ofn.structSize = Marshal.SizeOf(ofn);
            ofn.filter = "OCX结构文件 (*.3docx;*.xml;*.ocx)\0*.3docx;*.xml;*.ocx\0所有文件 (*.*)\0*.*\0";
            int bufferChars = 16384;
            int titleChars = 512;
            IntPtr fileBuf = IntPtr.Zero;
            IntPtr titleBuf = IntPtr.Zero;
            try
            {
                fileBuf = Marshal.AllocHGlobal(bufferChars * sizeof(char));
                titleBuf = Marshal.AllocHGlobal(titleChars * sizeof(char));
                ZeroMemory(fileBuf, bufferChars * sizeof(char));
                ZeroMemory(titleBuf, titleChars * sizeof(char));
                ofn.file = fileBuf;
                ofn.maxFile = bufferChars;
                ofn.fileTitle = titleBuf;
                ofn.maxFileTitle = titleChars;
                ofn.title = "导入本地 OCX 文件（可多选）";

                ofn.flags = OFN_EXPLORER | OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR | OFN_ALLOWMULTISELECT;

                string currentUnityDirectory = Environment.CurrentDirectory;
                bool success = GetOpenFileName(ofn);
                Environment.CurrentDirectory = currentUnityDirectory;

                if (!success) return Array.Empty<string>();

                var parts = ReadMultiString(fileBuf, bufferChars);
                if (parts.Length == 0) return Array.Empty<string>();
                if (parts.Length == 1) return new[] { parts[0] };

                string dir = parts[0];
                var list = new string[parts.Length - 1];
                for (int i = 1; i < parts.Length; i++)
                {
                    list[i - 1] = Path.Combine(dir, parts[i]);
                }
                return list;
            }
            finally
            {
                if (fileBuf != IntPtr.Zero) Marshal.FreeHGlobal(fileBuf);
                if (titleBuf != IntPtr.Zero) Marshal.FreeHGlobal(titleBuf);
            }
    }

        private static string OpenSingleFile(string filter, string title)
        {
            OpenFileName ofn = new OpenFileName();
            ofn.structSize = Marshal.SizeOf(ofn);
            ofn.filter = filter;
            int bufferChars = 4096;
            int titleChars = 512;
            IntPtr fileBuf = IntPtr.Zero;
            IntPtr titleBuf = IntPtr.Zero;
            try
            {
                fileBuf = Marshal.AllocHGlobal(bufferChars * sizeof(char));
                titleBuf = Marshal.AllocHGlobal(titleChars * sizeof(char));
                ZeroMemory(fileBuf, bufferChars * sizeof(char));
                ZeroMemory(titleBuf, titleChars * sizeof(char));
                ofn.file = fileBuf;
                ofn.maxFile = bufferChars;
                ofn.fileTitle = titleBuf;
                ofn.maxFileTitle = titleChars;
                ofn.title = title;
                ofn.flags = OFN_EXPLORER | OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR;

                string currentUnityDirectory = Environment.CurrentDirectory;
                bool success = GetOpenFileName(ofn);
                Environment.CurrentDirectory = currentUnityDirectory;

                if (!success) return null;
                string result = Marshal.PtrToStringAuto(fileBuf);
                return string.IsNullOrWhiteSpace(result) ? null : result;
            }
            finally
            {
                if (fileBuf != IntPtr.Zero) Marshal.FreeHGlobal(fileBuf);
                if (titleBuf != IntPtr.Zero) Marshal.FreeHGlobal(titleBuf);
            }
        }

        private static void ZeroMemory(IntPtr ptr, int bytes)
        {
            if (ptr == IntPtr.Zero || bytes <= 0) return;
            byte[] zero = new byte[bytes];
            Marshal.Copy(zero, 0, ptr, bytes);
        }

        private static void WriteStringToBuffer(IntPtr ptr, int maxChars, string text)
        {
            if (ptr == IntPtr.Zero || maxChars <= 0) return;
            if (string.IsNullOrEmpty(text)) return;
            var chars = text.ToCharArray();
            int count = Math.Min(chars.Length, maxChars - 1);
            Marshal.Copy(chars, 0, ptr, count);
            Marshal.WriteInt16(ptr, count * sizeof(char), 0);
        }

        private static string[] ReadMultiString(IntPtr ptr, int maxChars)
        {
            if (ptr == IntPtr.Zero || maxChars <= 0) return Array.Empty<string>();
            var chars = new char[maxChars];
            Marshal.Copy(ptr, chars, 0, maxChars);

            var list = new System.Collections.Generic.List<string>();
            int start = 0;
            for (int i = 0; i < chars.Length; i++)
            {
                if (chars[i] != '\0') continue;
                int len = i - start;
                if (len == 0) break;
                list.Add(new string(chars, start, len));
                start = i + 1;
            }

            return list.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        }
}
}
