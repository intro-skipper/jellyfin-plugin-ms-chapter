using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using ChapterCreator.Data;

namespace ChapterCreator.Helper;

/// <summary>
/// Provides functionality for writing MKV chapter files in XML format.
/// </summary>
public static class MKVChapterWriter
{
    /// <summary>
    /// Creates an XML file containing MKV chapter information.
    /// </summary>
    /// <param name="filename">The filename to write the chapter file to.</param>
    /// <param name="chapters">List of chapters to write to the file.</param>
    /// <param name="overwrite">Force the file overwrite.</param>
    public static void CreateChapterXmlFile(string filename, IReadOnlyList<Chapter> chapters, bool overwrite)
    {
        if (chapters is null || chapters.Count == 0)
        {
            return;
        }

        if (File.Exists(filename) && !overwrite)
        {
            return;
        }

        // Generate random UIDs for the Edition and Chapters
        long editionUID = GenerateUID();
        long[] chapterUIDs = [.. Enumerable.Range(0, chapters.Count).Select(_ => GenerateUID())];

        // Create an XML writer with appropriate settings
        XmlWriterSettings settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            NewLineOnAttributes = false
        };

        using XmlWriter writer = XmlWriter.Create(filename, settings);
        // Start the Matroska chapters document
        writer.WriteStartDocument();
        writer.WriteStartElement("Chapters");

        // Create an EditionEntry with EditionUID
        writer.WriteStartElement("EditionEntry");
        writer.WriteElementString("EditionUID", editionUID.ToString(System.Globalization.CultureInfo.InvariantCulture));

        // Write each chapter
        for (int i = 0; i < chapters.Count; i++)
        {
            writer.WriteStartElement("ChapterAtom");
            // Write ChapterUID and ChapterTimeStart
            writer.WriteElementString("ChapterUID", chapterUIDs[i].ToString(System.Globalization.CultureInfo.InvariantCulture));
            writer.WriteElementString("ChapterTimeStart", chapters[i].StartTime);

            // Write ChapterDisplay element with ChapterString
            writer.WriteStartElement("ChapterDisplay");
            writer.WriteElementString("ChapterString", chapters[i].Title);
            writer.WriteElementString("ChapterLanguage", "und");
            writer.WriteEndElement(); // End ChapterDisplay

            writer.WriteEndElement(); // End ChapterAtom
        }

        writer.WriteEndElement(); // End EditionEntry
        writer.WriteEndElement(); // End Chapters
        writer.WriteEndDocument();
    }

    // Method to generate a random UID (Matroska recommends 64-bit unsigned integers)
    private static long GenerateUID()
    {
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        byte[] buffer = new byte[8];
        rng.GetBytes(buffer);
        return BitConverter.ToInt64(buffer, 0) & 0x7FFFFFFFFFFFFFFF; // Ensure positive number
    }
}
