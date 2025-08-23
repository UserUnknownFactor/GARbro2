using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace SchemeTool
{
    class SchemeConverter
    {
        static void Main(string[] args)
        {
            if (args.Length < 2)
            {
                PrintUsage();
                return;
            }

            string inputFile = args[0];
            string outputFile = args[1];

            if (!File.Exists(inputFile))
            {
                Console.WriteLine($"Error: Input file '{inputFile}' not found.");
                return;
            }

            bool verbose = args.Any(a => a.Equals("--verbose", StringComparison.OrdinalIgnoreCase) || a.Equals("-v", StringComparison.OrdinalIgnoreCase));
            bool debug = args.Any(a => a.Equals("--debug", StringComparison.OrdinalIgnoreCase));

            try
            {
                int originalVersion = GameRes.FormatCatalog.Instance.CurrentSchemeVersion;

                using (Stream inputStream = File.OpenRead(inputFile))
                {
                    if (inputFile.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine("Loading from JSON format...");
                        GameRes.FormatCatalog.Instance.DeserializeSchemeJson(inputStream);
                    }
                    else
                    {
                        Console.WriteLine("Loading from binary format...");
                        GameRes.FormatCatalog.Instance.DeserializeScheme(inputStream);
                    }
                }

                // Create a SchemeDataBase to hold the data
                var db = new GameRes.SchemeDataBase
                {
                    Version = GameRes.FormatCatalog.Instance.CurrentSchemeVersion,
                    SchemeMap = new Dictionary<string, GameRes.ResourceScheme>(),
                    GameMap = GetGameMap()
                };

                // Collect all schemes from the catalog
                foreach (var format in GameRes.FormatCatalog.Instance.Formats)
                {
                    var scheme = format.Scheme;
                    if (scheme != null)
                    {
                        db.SchemeMap[format.Tag] = scheme;
                    }
                }

                using (Stream outputStream = File.Create(outputFile))
                {
                    if (outputFile.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine("Saving to JSON format...");
                        GameRes.FormatCatalog.Instance.SerializeSchemeJson(outputStream, db);
                    }
                    else
                    {
                        Console.WriteLine("Saving to binary format...");
                        GameRes.FormatCatalog.Instance.SerializeScheme(outputStream, db);
                    }
                }

                Console.WriteLine($"Successfully converted '{inputFile}' to '{outputFile}'");

                if (verbose)
                {
                    Console.WriteLine();
                    Console.WriteLine($"Database version: {db.Version}");
                    Console.WriteLine($"Schemes: {db.SchemeMap.Count}");
                    Console.WriteLine($"Game mappings: {db.GameMap?.Count ?? 0}");

                    Console.WriteLine();
                    Console.WriteLine("Schemes:");
                    foreach (var scheme in db.SchemeMap)
                    {
                        Console.WriteLine($"  {scheme.Key}: {scheme.Value.GetType().Name}");
                    }
                }

                if (originalVersion != GameRes.FormatCatalog.Instance.CurrentSchemeVersion)
                {
                    Console.WriteLine($"Note: Database version changed from {originalVersion} to {GameRes.FormatCatalog.Instance.CurrentSchemeVersion}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                if (debug)
                {
                    Console.WriteLine();
                    Console.WriteLine("Stack trace:");
                    Console.WriteLine(ex.StackTrace);
                }
            }
        }

        // Helper method to get the game map from FormatCatalog using reflection
        private static Dictionary<string, string> GetGameMap()
        {
            try
            {
                var field = typeof(GameRes.FormatCatalog).GetField("m_game_map",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

                if (field != null)
                {
                    return field.GetValue(GameRes.FormatCatalog.Instance) as Dictionary<string, string>;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not access game map: {ex.Message}");
            }

            return new Dictionary<string, string>();
        }

        static void PrintUsage()
        {
            Console.WriteLine("Scheme Format Converter");
            Console.WriteLine("Usage: SchemeConverter.exe <input_file> <output_file> [options]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --verbose, -v    Show detailed information about the database");
            Console.WriteLine("  --debug          Show stack trace on error");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  SchemeConverter.exe Formats.dat Formats.json   - Convert binary to JSON");
            Console.WriteLine("  SchemeConverter.exe Formats.json Formats.dat   - Convert JSON to binary");
            Console.WriteLine("  SchemeConverter.exe Formats.dat Formats.json --verbose  - Show scheme details");
            Console.WriteLine();
            Console.WriteLine("The format is determined by the file extension.");
        }
    }
}