using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Typedown.Core.Utilities
{
    public static class FileTypeHelper
    {
        public static HashSet<string> Markdown { get; } = new() { ".md", ".markdown", ".mdown", ".mkdn", ".mkd", ".mdwn", ".txt", ".mdtxt", ".text", ".mdtext", ".rmd" };

        public static HashSet<string> Image = new() { ".jpeg", ".jpg", ".png", ".gif", ".svg", ".webp", ".jfif" };

        // Plain-text files the editor can open and edit as Markdown text. Shown in the file tree alongside
        // Markdown so a folder of notes with the odd .txt or .log is not half-hidden (a Store review).
        // Config and data files (.ini, .json, ...: a reader's request, 2026-11-04) are plain text too, opened as they are.
        public static HashSet<string> PlainText { get; } = new() { ".txt", ".text", ".log", ".markdown", ".ini", ".cfg", ".conf", ".json", ".toml", ".yaml", ".yml", ".xml", ".csv", ".tsv" };

        public enum FileType
        {
            Unknown,
            Markdown,
            Image,
            PlainText
        }

        public static FileType GetFileType(string fileName)
        {
            if(!string.IsNullOrEmpty(fileName) && fileName.Contains('.'))
            {
                var ex = Path.GetExtension(fileName).ToLower();
                if (Markdown.Contains(ex))
                    return FileType.Markdown;
                if (Image.Contains(ex))
                    return FileType.Image;
                if (PlainText.Contains(ex))
                    return FileType.PlainText;
            }
            return FileType.Unknown;
        }

        public static bool IsMarkdownFile(string fileName)
        {
            return GetFileType(fileName) == FileType.Markdown;
        }

        /// <summary>Markdown or a plain-text file the editor can open — what the file tree shows.</summary>
        public static bool IsEditableTextFile(string fileName)
        {
            var type = GetFileType(fileName);
            return type == FileType.Markdown || type == FileType.PlainText;
        }

        // Text files that are not Markdown: shown as they are, with nothing in them parsed as Markdown (a reader's
        // request, 2026-11-02) - a log's "#" or "*" is no heading or emphasis, and the visual editor would rewrite them.
        private static readonly HashSet<string> plainTextDocuments = new() { ".txt", ".text", ".log", ".ini", ".cfg", ".conf", ".json", ".toml", ".yaml", ".yml", ".xml", ".csv", ".tsv" };

        /// <summary>A plain text file: shown in source mode with no Markdown highlighting, its text saved as it is.</summary>
        public static bool IsPlainTextDocument(string fileName) =>
            !string.IsNullOrEmpty(fileName) && plainTextDocuments.Contains(Path.GetExtension(fileName).ToLowerInvariant());

        public static bool IsImageFile(string fileName)
        {
            return GetFileType(fileName) == FileType.Image;
        }
    }
}
