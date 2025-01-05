using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using ChapterCreator.Data;
using Microsoft.Extensions.Logging;

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
    /// <param name="logger">The logger to use for logging.</param>
    public static void CreateChapterXmlFile(string filename, IReadOnlyList<Chapter> chapters, bool overwrite, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(filename);

        if (chapters is null || chapters.Count == 0)
        {
            logger?.LogDebug("No chapters provided for file {Filename}", filename);
            return;
        }

        if (File.Exists(filename))
        {
            if (!overwrite)
            {
                logger?.LogDebug("Skipping existing chapter file {Filename} (overwrite disabled)", filename);
                return;
            }

            logger?.LogDebug("Overwriting existing chapter file {Filename}", filename);
        }

        var directoryPath = Path.GetDirectoryName(filename)!;
        Directory.CreateDirectory(directoryPath);
        logger?.LogDebug("Ensuring directory exists: {Directory}", directoryPath);

        // Generate random UIDs for the Edition and Chapters
        long editionUID = GenerateUID();
        long[] chapterUIDs = [.. Enumerable.Range(0, chapters.Count).Select(_ => GenerateUID())];

        logger?.LogDebug("Writing {Count} chapters to {Filename}", chapters.Count, filename);

        // Create an XML writer with appropriate settings
        XmlWriterSettings settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            NewLineOnAttributes = false
        };

        using XmlWriter writer = XmlWriter.Create(filename, settings);

        // Write the XML structure using constants for element names
        writer.WriteStartDocument();
        writer.WriteStartElement("Chapters");
        {
            writer.WriteStartElement("EditionEntry");
            {
                writer.WriteElementString("EditionUID", editionUID.ToString(System.Globalization.CultureInfo.InvariantCulture));
                writer.WriteElementString("EditionFlagDefault", "1");  // Add default flag
                writer.WriteElementString("EditionFlagHidden", "0");   // Add hidden flag

                // Write each chapter
                for (int i = 0; i < chapters.Count; i++)
                {
                    WriteChapterAtom(writer, chapters[i], chapterUIDs[i]);
                }
            }

            writer.WriteEndElement(); // End EditionEntry
        }

        writer.WriteEndElement(); // End Chapters
        writer.WriteEndDocument();
    }

    private static void WriteChapterAtom(XmlWriter writer, Chapter chapter, long chapterUID)
    {
        writer.WriteStartElement("ChapterAtom");
        {
            writer.WriteElementString("ChapterUID", chapterUID.ToString(System.Globalization.CultureInfo.InvariantCulture));
            writer.WriteElementString("ChapterFlagHidden", "0");
            writer.WriteElementString("ChapterFlagEnabled", "1");
            writer.WriteElementString("ChapterTimeStart", chapter.StartTime);
            writer.WriteElementString("ChapterTimeEnd", chapter.EndTime);

            writer.WriteStartElement("ChapterDisplay");
            {
                writer.WriteElementString("ChapterString", chapter.Title);
                writer.WriteElementString("ChapterLanguage", "und");
            }

            writer.WriteEndElement(); // End ChapterDisplay
        }

        writer.WriteEndElement(); // End ChapterAtom
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
