using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EAStudio.Core.Editor.TextureAnalysis
{
    [Flags]
    public enum TextureIssueFlags
    {
        None = 0,
        NonPowerOfTwo = 1 << 0,
        Uncompressed = 1 << 1,
        HighResolution = 1 << 2,
        MissingMipmaps = 1 << 3,
        UnnecessaryMipmaps = 1 << 4,
        ReadOnlyViolation = 1 << 5, // Read/Write enabled
        DuplicateName = 1 << 6
    }

    public enum TextureScanScope
    {
        CurrentScene,
        BuildScenes
    }

    [Serializable]
    public class TextureUsageEntry
    {
        public string AssetPath;
        public string TextureName;
        public int Width;
        public int Height;
        public TextureFormat Format;
        public TextureImporterType TextureType;
        public bool HasMipmaps;
        public int MipmapCount;
        public bool IsReadable;
        public bool IsPowerOfTwo;
        public long MemorySizeBytes;
        public long StorageSizeBytes;

        public TextureIssueFlags Issues;
        public List<string> ReferencingScenes = new List<string>();

        public string ResolutionString => $"{Width}x{Height}";
        public string MemorySizeFormatted => FormatBytes(MemorySizeBytes);
        public string StorageSizeFormatted => FormatBytes(StorageSizeBytes);

        public static string FormatBytes(long bytes)
        {
            if (bytes < 1024)
                return $"{bytes} B";
            if (bytes < 1024 * 1024)
                return $"{(bytes / 1024f):F1} KB";
            return $"{(bytes / (1024f * 1024f)):F2} MB";
        }
    }

    [Serializable]
    public class TextureReport
    {
        public DateTime ScanTime;
        public TextureScanScope Scope;
        public List<string> ScannedScenePaths = new List<string>();
        public List<TextureUsageEntry> Entries = new List<TextureUsageEntry>();

        public int TotalTextureCount => Entries.Count;
        public long TotalMemorySizeBytes
        {
            get
            {
                long sum = 0;
                foreach (var entry in Entries)
                    sum += entry.MemorySizeBytes;
                return sum;
            }
        }

        public int TotalHighResCount
        {
            get
            {
                int c = 0;
                foreach (var entry in Entries)
                    if ((entry.Issues & TextureIssueFlags.HighResolution) != 0) c++;
                return c;
            }
        }

        public int TotalUncompressedCount
        {
            get
            {
                int c = 0;
                foreach (var entry in Entries)
                    if ((entry.Issues & TextureIssueFlags.Uncompressed) != 0) c++;
                return c;
            }
        }

        public int TotalMissingMipmapsCount
        {
            get
            {
                int c = 0;
                foreach (var entry in Entries)
                    if ((entry.Issues & TextureIssueFlags.MissingMipmaps) != 0) c++;
                return c;
            }
        }

        public int TotalReadableCount
        {
            get
            {
                int c = 0;
                foreach (var entry in Entries)
                    if ((entry.Issues & TextureIssueFlags.ReadOnlyViolation) != 0) c++;
                return c;
            }
        }
    }
}
