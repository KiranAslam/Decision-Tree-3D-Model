using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace DecisionTreeViz
{
    public sealed class FeatureOption
    {
        public int Code;       // number the model was trained on, e.g. 3
        public string Name;    // readable name, e.g. "Reversible defect"
    }

    public sealed class FeatureMeta
    {
        public string Key;                                            // "thal"
        public string Label;                                          // "Thalassemia test"
        public List<FeatureOption> Options = new List<FeatureOption>(); // empty for numeric features
    }

    public sealed class PatientSample
    {
        public Dictionary<string, double> Features = new Dictionary<string, double>();
        public int TrueLabel;       // what the dataset says
        public int Prediction;      // what the model said
    }

    /// <summary>Reads the JSON files that train_model.py wrote into Assets/Resources.</summary>
    public static class PanelData
    {
        public static Dictionary<string, FeatureMeta> LoadFeatures(string resourceName)
        {
            var result = new Dictionary<string, FeatureMeta>();

            TextAsset asset = Resources.Load<TextAsset>(resourceName);
            if (asset == null)
            {
                Debug.LogWarning("[PanelData] Resources/" + resourceName + ".json not found.");
                return result;
            }

            var root = MiniJson.Parse(asset.text) as Dictionary<string, object>;
            Dictionary<string, object> features = null;
            if (root != null && root.ContainsKey("features"))
                features = root["features"] as Dictionary<string, object>;
            if (features == null) return result;

            foreach (KeyValuePair<string, object> kv in features)
            {
                var f = kv.Value as Dictionary<string, object>;
                if (f == null) continue;

                var meta = new FeatureMeta();
                meta.Key = kv.Key;
                meta.Label = f.ContainsKey("label") && f["label"] != null ? Convert.ToString(f["label"]) : kv.Key;

                Dictionary<string, object> values = null;
                if (f.ContainsKey("values")) values = f["values"] as Dictionary<string, object>;
                if (values != null)
                {
                    foreach (KeyValuePair<string, object> vkv in values)
                    {
                        int code;
                        if (!int.TryParse(vkv.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out code)) continue;
                        var option = new FeatureOption();
                        option.Code = code;
                        option.Name = Convert.ToString(vkv.Value);
                        meta.Options.Add(option);
                    }
                    meta.Options.Sort((a, b) => a.Code.CompareTo(b.Code));
                }

                result[kv.Key] = meta;
            }

            return result;
        }

        public static List<PatientSample> LoadSamples(string resourceName)
        {
            var list = new List<PatientSample>();

            TextAsset asset = Resources.Load<TextAsset>(resourceName);
            if (asset == null)
            {
                Debug.LogWarning("[PanelData] Resources/" + resourceName + ".json not found.");
                return list;
            }

            var root = MiniJson.Parse(asset.text) as Dictionary<string, object>;
            List<object> samples = null;
            if (root != null && root.ContainsKey("samples")) samples = root["samples"] as List<object>;
            if (samples == null) return list;

            foreach (object o in samples)
            {
                var d = o as Dictionary<string, object>;
                if (d == null) continue;

                var s = new PatientSample();

                Dictionary<string, object> feats = null;
                if (d.ContainsKey("features")) feats = d["features"] as Dictionary<string, object>;
                if (feats != null)
                {
                    foreach (KeyValuePair<string, object> kv in feats)
                    {
                        if (kv.Value is double) s.Features[kv.Key] = (double)kv.Value;
                    }
                }

                s.TrueLabel = ReadInt(d, "true_label");
                s.Prediction = ReadInt(d, "prediction");
                list.Add(s);
            }

            return list;
        }

        private static int ReadInt(Dictionary<string, object> d, string key)
        {
            object v;
            return d.TryGetValue(key, out v) && v is double ? (int)Math.Round((double)v) : 0;
        }
    }
}