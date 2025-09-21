using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Buds3ProAideAuditiveIA.v2
{
    public sealed class PresetManager
    {
        private readonly string _folder;
        private readonly JsonSerializerSettings _jsonSettings;

        public PresetManager(string folder)
        {
            _folder = folder ?? throw new ArgumentNullException(nameof(folder));
            Directory.CreateDirectory(_folder);

            _jsonSettings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                ContractResolver = new DefaultContractResolver
                {
                    NamingStrategy = new CamelCaseNamingStrategy()
                },
                NullValueHandling = NullValueHandling.Ignore
            };
        }

        public string GetPresetPath(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Preset name required", nameof(name));
            var safe = string.Concat(name.Where(ch => char.IsLetterOrDigit(ch) || ch=='-' || ch=='_' ));
            if (string.IsNullOrWhiteSpace(safe)) safe = "preset";
            return Path.Combine(_folder, $"{safe}.json");
        }

        public void Save(string name, AudioPresetData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var path = GetPresetPath(name);
            var json = JsonConvert.SerializeObject(data, _jsonSettings);
            File.WriteAllText(path, json);
        }

        public AudioPresetData Load(string name)
        {
            var path = GetPresetPath(name);
            if (!File.Exists(path)) throw new FileNotFoundException("Preset not found", path);
            var json = File.ReadAllText(path);
            var data = JsonConvert.DeserializeObject<AudioPresetData>(json, _jsonSettings);
            if (data == null) throw new InvalidDataException("Invalid preset json");
            return data;
        }

        public IEnumerable<string> ListNames()
        {
            return Directory
                .EnumerateFiles(_folder, "*.json", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
        }

        public bool Delete(string name)
        {
            var path = GetPresetPath(name);
            if (File.Exists(path)) { File.Delete(path); return true; }
            return false;
        }

        public string ExportAllToSingleFile(string exportPath)
        {
            var all = new Dictionary<string, AudioPresetData>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in ListNames())
            {
                all[name] = Load(name);
            }
            var json = JsonConvert.SerializeObject(all, _jsonSettings);
            File.WriteAllText(exportPath, json);
            return exportPath;
        }

        public int ImportFromSingleFile(string importPath, bool overwrite = false)
        {
            if (!File.Exists(importPath)) throw new FileNotFoundException(importPath);
            var json = File.ReadAllText(importPath);
            var dict = JsonConvert.DeserializeObject<Dictionary<string, AudioPresetData>>(json, _jsonSettings)
                       ?? new Dictionary<string, AudioPresetData>();
            int count = 0;
            foreach (var kv in dict)
            {
                var path = GetPresetPath(kv.Key);
                if (!overwrite && File.Exists(path)) continue;
                Save(kv.Key, kv.Value);
                count++;
            }
            return count;
        }
    }

    // Data contract for a complete configuration snapshot (extend freely)
    public sealed class AudioPresetData
    {
        // Gains & EQ
        public int MasterGainDb { get; set; } = 0;
        public int HighpassHz { get; set; } = 0;
        public bool HighpassEnabled { get; set; } = false;
        public bool NoiseReductionEnabled { get; set; } = false;

        // Gate/Ducker
        public double GateThresholdDb { get; set; } = -50;
        public int GateReleaseMs { get; set; } = 250;
        public int AmbientAttackMs { get; set; } = 150; // <- important: the property referenced in your codebase

        // Latency profile
        public string LatencyProfile { get; set; } = "Balanced";

        // Routing
        public string BluetoothRoute { get; set; } = "A2DP"; // A2DP|SCO|LE

        // Additional flags
        public bool AecEnabled { get; set; } = false;
        public bool AgcEnabled { get; set; } = false;
    }
}
