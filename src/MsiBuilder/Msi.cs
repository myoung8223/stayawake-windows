using System;
using System.Runtime.InteropServices;

namespace StayAwake.MsiBuilder
{
    /// <summary>Thin wrapper over Windows Installer's own database API (msi.dll, built into Windows).</summary>
    sealed class MsiDatabase : IDisposable
    {
        const int ERROR_SUCCESS = 0;
        static readonly IntPtr MSIDBOPEN_CREATE = new IntPtr(3);

        [DllImport("msi.dll", CharSet = CharSet.Unicode)] static extern int MsiOpenDatabaseW(string path, IntPtr persist, out IntPtr db);
        [DllImport("msi.dll", CharSet = CharSet.Unicode)] static extern int MsiDatabaseOpenViewW(IntPtr db, string query, out IntPtr view);
        [DllImport("msi.dll")] static extern int MsiViewExecute(IntPtr view, IntPtr record);
        [DllImport("msi.dll")] static extern int MsiViewClose(IntPtr view);
        [DllImport("msi.dll")] static extern int MsiCloseHandle(IntPtr handle);
        [DllImport("msi.dll")] static extern IntPtr MsiCreateRecord(uint fields);
        [DllImport("msi.dll", CharSet = CharSet.Unicode)] static extern int MsiRecordSetStringW(IntPtr record, uint field, string value);
        [DllImport("msi.dll")] static extern int MsiRecordSetInteger(IntPtr record, uint field, int value);
        [DllImport("msi.dll", CharSet = CharSet.Unicode)] static extern int MsiRecordSetStreamW(IntPtr record, uint field, string filePath);
        [DllImport("msi.dll")] static extern int MsiDatabaseCommit(IntPtr db);
        [DllImport("msi.dll", CharSet = CharSet.Unicode)] static extern int MsiGetSummaryInformationW(IntPtr db, string path, uint updateCount, out IntPtr summary);
        [DllImport("msi.dll", CharSet = CharSet.Unicode, EntryPoint = "MsiSummaryInfoSetPropertyW")]
        static extern int SummarySetString(IntPtr summary, uint property, uint dataType, int intValue, IntPtr fileTime, string value);
        [DllImport("msi.dll", CharSet = CharSet.Unicode, EntryPoint = "MsiSummaryInfoSetPropertyW")]
        static extern int SummarySetTime(IntPtr summary, uint property, uint dataType, int intValue, ref System.Runtime.InteropServices.ComTypes.FILETIME fileTime, string value);
        [DllImport("msi.dll")] static extern int MsiSummaryInfoPersist(IntPtr summary);
        [DllImport("msi.dll")] static extern IntPtr MsiGetLastErrorRecord();
        [DllImport("msi.dll", CharSet = CharSet.Unicode)] static extern int MsiFormatRecordW(IntPtr install, IntPtr record, System.Text.StringBuilder result, ref int size);

        const uint VT_I2 = 2, VT_I4 = 3, VT_LPSTR = 30, VT_FILETIME = 64;

        IntPtr _db;

        public static MsiDatabase Create(string path)
        {
            var d = new MsiDatabase();
            Check(MsiOpenDatabaseW(path, MSIDBOPEN_CREATE, out d._db), "create " + path);
            return d;
        }

        /// <summary>Runs a statement such as CREATE TABLE.</summary>
        public void Execute(string sql)
        {
            IntPtr view;
            Check(MsiDatabaseOpenViewW(_db, sql, out view), sql);
            try { Check(MsiViewExecute(view, IntPtr.Zero), sql); }
            finally { MsiViewClose(view); MsiCloseHandle(view); }
        }

        /// <summary>Inserts one row. Values may be string, int, or null (left NULL).</summary>
        public void Insert(string table, string[] columns, object[] values)
        {
            var cols = "`" + string.Join("`,`", columns) + "`";
            var marks = string.Join(",", System.Linq.Enumerable.Repeat("?", columns.Length));
            string sql = "INSERT INTO `" + table + "` (" + cols + ") VALUES (" + marks + ")";
            IntPtr view, rec = MsiCreateRecord((uint)columns.Length);
            try
            {
                for (int i = 0; i < values.Length; i++)
                {
                    uint f = (uint)(i + 1);
                    object v = values[i];
                    if (v == null) continue;
                    if (v is int n) Check(MsiRecordSetInteger(rec, f, n), sql);
                    else Check(MsiRecordSetStringW(rec, f, (string)v), sql);
                }
                Check(MsiDatabaseOpenViewW(_db, sql, out view), sql);
                try { Check(MsiViewExecute(view, rec), table + ": " + string.Join(" | ", values)); }
                finally { MsiViewClose(view); MsiCloseHandle(view); }
            }
            finally { MsiCloseHandle(rec); }
        }

        /// <summary>Embeds a file (the cabinet) as a named stream.</summary>
        public void AddStream(string name, string filePath)
        {
            const string sql = "INSERT INTO `_Streams` (`Name`,`Data`) VALUES (?,?)";
            IntPtr view, rec = MsiCreateRecord(2);
            try
            {
                Check(MsiRecordSetStringW(rec, 1, name), sql);
                Check(MsiRecordSetStreamW(rec, 2, filePath), "stream " + filePath);
                Check(MsiDatabaseOpenViewW(_db, sql, out view), sql);
                try { Check(MsiViewExecute(view, rec), sql); }
                finally { MsiViewClose(view); MsiCloseHandle(view); }
            }
            finally { MsiCloseHandle(rec); }
        }

        public void WriteSummary(string title, string subject, string author, string comments, string template, string packageCode)
        {
            IntPtr si;
            Check(MsiGetSummaryInformationW(_db, null, 20, out si), "summary info");
            try
            {
                var now = DateTime.Now.ToFileTime();
                var ft = new System.Runtime.InteropServices.ComTypes.FILETIME { dwLowDateTime = (int)(now & 0xffffffff), dwHighDateTime = (int)(now >> 32) };
                Check(SummarySetString(si, 1, VT_I2, 1252, IntPtr.Zero, null), "codepage");
                Check(SummarySetString(si, 2, VT_LPSTR, 0, IntPtr.Zero, title), "title");
                Check(SummarySetString(si, 3, VT_LPSTR, 0, IntPtr.Zero, subject), "subject");
                Check(SummarySetString(si, 4, VT_LPSTR, 0, IntPtr.Zero, author), "author");
                Check(SummarySetString(si, 5, VT_LPSTR, 0, IntPtr.Zero, "Installer"), "keywords");
                Check(SummarySetString(si, 6, VT_LPSTR, 0, IntPtr.Zero, comments), "comments");
                Check(SummarySetString(si, 7, VT_LPSTR, 0, IntPtr.Zero, template), "template");
                Check(SummarySetString(si, 9, VT_LPSTR, 0, IntPtr.Zero, packageCode), "package code");
                Check(SummarySetTime(si, 12, VT_FILETIME, 0, ref ft, null), "create time");
                Check(SummarySetTime(si, 13, VT_FILETIME, 0, ref ft, null), "save time");
                Check(SummarySetString(si, 14, VT_I4, 200, IntPtr.Zero, null), "page count");     // installer 2.0+, needed for x64
                Check(SummarySetString(si, 15, VT_I4, 2, IntPtr.Zero, null), "word count");       // compressed, long names, elevated
                Check(SummarySetString(si, 18, VT_LPSTR, 0, IntPtr.Zero, "Stay Awake MSI Builder"), "app name");
                Check(SummarySetString(si, 19, VT_I4, 2, IntPtr.Zero, null), "security");
                Check(MsiSummaryInfoPersist(si), "persist summary");
            }
            finally { MsiCloseHandle(si); }
        }

        public void Commit() { Check(MsiDatabaseCommit(_db), "commit"); }

        public void Dispose()
        {
            if (_db != IntPtr.Zero) { MsiCloseHandle(_db); _db = IntPtr.Zero; }
        }

        static void Check(int rc, string what)
        {
            if (rc == ERROR_SUCCESS) return;
            string detail = "";
            IntPtr err = MsiGetLastErrorRecord();
            if (err != IntPtr.Zero)
            {
                var sb = new System.Text.StringBuilder(1024);
                int size = sb.Capacity;
                if (MsiFormatRecordW(IntPtr.Zero, err, sb, ref size) == 0) detail = " (" + sb + ")";
                MsiCloseHandle(err);
            }
            throw new InvalidOperationException("MSI error " + rc + " during: " + what + detail);
        }
    }
}
