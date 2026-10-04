using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DecisionTreeViz
{
    /// <summary>
    /// Connects the "Patient parameters" panel to the 3D tree.
    ///
    /// Attach to: any object inside the DT panel (for example Panel_02_Model_DT).
    /// Everything is wired by dragging objects into the Inspector - no object names are needed.
    ///
    /// What it does:
    ///  - fills the dropdowns with the category names from feature_info.json (so the names always match the model)
    ///  - sends the chosen values through the real tree (TreeInference)
    ///  - asks PathHighlighter to light that path and fade the rest of the tree
    ///  - fills the result box with the leaf's prediction
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class ParameterPanelController : MonoBehaviour
    {
        [Serializable]
        public class Binding
        {
            [Tooltip("Feature key exactly as in the JSON: cp, ca, thal, slope ...")]
            public string feature;
            [Tooltip("Use this when the feature is a dropdown...")]
            public TMP_Dropdown dropdown;
            [Tooltip("...or this when it is a row of buttons, in the order of the codes (for ca: 0, 1, 2, 3).")]
            public Button[] buttons;

            [NonSerialized] public List<FeatureOption> options;
        }

        [Header("Tree")]
        [SerializeField] private DecisionTreeVisualizer visualizer;
        [SerializeField] private PathHighlighter highlighter;

        [Header("Parameters")]
        [SerializeField] private Binding[] bindings;
        [Tooltip("The first dropdown ('Custom Values'). Optional.")]
        [SerializeField] private TMP_Dropdown sampleDropdown;

        [Header("Buttons (all optional)")]
        [SerializeField] private Button runButton;
        [SerializeField] private Button stepButton;
        [SerializeField] private Button resetButton;

        [Header("Result box (all optional)")]
        [SerializeField] private TMP_Text resultTitle;
        [SerializeField] private TMP_Text resultBody;
        [Tooltip("Optional multi-line text that lists every split of the path.")]
        [SerializeField] private TMP_Text pathText;
        [SerializeField] private string placeholderTitle = "RESULT";

        [Header("Behaviour")]
        [Tooltip("On: the path updates the moment a parameter changes. Off: only when Run or Step is pressed.")]
        [SerializeField] private bool liveUpdate = true;
        [Tooltip("Light the path of the starting values as soon as the tree appears.")]
        [SerializeField] private bool highlightOnStart = true;
        [SerializeField] private float secondsPerStep = 0.7f;
        [Tooltip("-1 = start with the first option of every parameter, 0 = test sample 1, ...")]
        [SerializeField] private int startSampleIndex = -1;

        [Header("Colours (use the same values as on the tree)")]
        [SerializeField] private Color heartDiseaseColor = new Color(1.00f, 0.62f, 0.26f);   // #FF9F43
        [SerializeField] private Color noDiseaseColor = new Color(0.37f, 0.90f, 0.66f);      // #5EE6A8
        [SerializeField] private Color placeholderColor = Color.white;
        [SerializeField] private Color buttonNormalColor = new Color(0.81f, 0.92f, 0.88f);
        [SerializeField] private Color buttonSelectedColor = new Color(0.36f, 0.79f, 0.65f);

        private TreeModel model;
        private List<PatientSample> samples = new List<PatientSample>();
        private readonly Dictionary<string, double> values = new Dictionary<string, double>();
        private readonly string[] className = { "No Heart Disease", "Heart Disease" };

        private TreeInference.Result current;
        private int shown;               // how many nodes of the current path are lit
        private int sampleIndex = -1;    // -1 = custom values
        private Coroutine runRoutine;
        private bool initialized;

        // ================================================================== start-up

        private void OnEnable()
        {
            runRoutine = null;
            if (!initialized) StartCoroutine(WaitAndInit());
        }

        private IEnumerator WaitAndInit()
        {
            // the tree is built by DecisionTreeVisualizer.Start, which only runs once DT_Model is active
            while (visualizer == null || visualizer.Model == null || visualizer.Views == null || visualizer.Views.Count == 0)
                yield return null;

            Init();
        }

        private void Init()
        {
            model = visualizer.Model;
            if (highlighter == null) highlighter = visualizer.GetComponent<PathHighlighter>();
            if (highlighter == null) Debug.LogError("[ParameterPanelController] No PathHighlighter found. Add one next to DecisionTreeVisualizer.", this);

            foreach (TreeNode n in model.AllNodes)
            {
                if (n.IsLeaf && n.Prediction >= 0 && n.Prediction < className.Length && !string.IsNullOrEmpty(n.PredictionLabel))
                    className[n.Prediction] = n.PredictionLabel;
            }

            Dictionary<string, FeatureMeta> meta = PanelData.LoadFeatures("feature_info");
            samples = PanelData.LoadSamples("sample_data");

            if (bindings != null)
            {
                foreach (Binding b in bindings) SetupBinding(b, meta);
            }
            CheckAllFeaturesAreBound();
            SetupSampleDropdown();

            if (runButton != null) runButton.onClick.AddListener(Run);
            if (stepButton != null) stepButton.onClick.AddListener(Step);
            if (resetButton != null) resetButton.onClick.AddListener(ResetView);

            initialized = true;

            if (startSampleIndex >= 0 && startSampleIndex < samples.Count)
            {
                ApplySample(startSampleIndex);
                if (sampleDropdown != null) sampleDropdown.SetValueWithoutNotify(startSampleIndex + 1);
            }
            else if (highlightOnStart)
            {
                ShowInstant();
            }
            else
            {
                ResetView();
            }
        }

        private void SetupBinding(Binding b, Dictionary<string, FeatureMeta> meta)
        {
            FeatureMeta m;
            if (string.IsNullOrEmpty(b.feature) || !meta.TryGetValue(b.feature, out m) || m.Options.Count == 0)
            {
                Debug.LogWarning("[ParameterPanelController] '" + b.feature +
                                 "' has no category list in feature_info.json (numeric features are not supported here).", this);
                return;
            }

            b.options = m.Options;
            string feature = b.feature;
            values[feature] = m.Options[0].Code;

            if (b.dropdown != null)
            {
                b.dropdown.ClearOptions();
                var names = new List<string>();
                foreach (FeatureOption o in m.Options) names.Add(o.Name);
                b.dropdown.AddOptions(names);
                Binding captured = b;
                b.dropdown.onValueChanged.AddListener(idx => OnChoice(feature, captured.options[idx].Code));
            }
            else if (b.buttons != null)
            {
                for (int i = 0; i < b.buttons.Length && i < m.Options.Count; i++)
                {
                    int code = m.Options[i].Code;
                    b.buttons[i].onClick.AddListener(() => OnChoice(feature, code));
                }
            }
            else
            {
                Debug.LogWarning("[ParameterPanelController] '" + b.feature + "' has neither a dropdown nor buttons assigned.", this);
            }

            RefreshBinding(b);
        }

        private void CheckAllFeaturesAreBound()
        {
            var missing = new HashSet<string>();
            foreach (TreeNode n in model.AllNodes)
            {
                if (!n.IsLeaf && !values.ContainsKey(n.Feature)) missing.Add(n.Feature);
            }
            if (missing.Count > 0)
                Debug.LogError("[ParameterPanelController] The tree splits on these features but the panel has no binding for them: " +
                               string.Join(", ", new List<string>(missing).ToArray()), this);
        }

        private void SetupSampleDropdown()
        {
            if (sampleDropdown == null) return;

            var names = new List<string>();
            names.Add("Custom Values");
            for (int i = 0; i < samples.Count; i++)
            {
                string line = "Test sample " + (i + 1) + " - dataset: " + ClassName(samples[i].TrueLabel);
                if (samples[i].Prediction != samples[i].TrueLabel) line += " (model wrong)";
                names.Add(line);
            }

            sampleDropdown.ClearOptions();
            sampleDropdown.AddOptions(names);
            sampleDropdown.SetValueWithoutNotify(0);
            sampleDropdown.onValueChanged.AddListener(OnSampleChosen);
        }

        // ================================================================== user input

        private void OnChoice(string feature, int code)
        {
            values[feature] = code;
            sampleIndex = -1;
            if (sampleDropdown != null) sampleDropdown.SetValueWithoutNotify(0);
            RefreshAllBindings();
            OnParametersChanged();
        }

        private void OnSampleChosen(int dropdownIndex)
        {
            if (dropdownIndex <= 0)
            {
                sampleIndex = -1;
                return;
            }
            ApplySample(dropdownIndex - 1);
        }

        private void ApplySample(int index)
        {
            PatientSample s = samples[index];
            if (bindings != null)
            {
                foreach (Binding b in bindings)
                {
                    double v;
                    if (b.options == null || !s.Features.TryGetValue(b.feature, out v)) continue;

                    int code = (int)Math.Round(v);
                    if (b.options.Exists(o => o.Code == code)) values[b.feature] = code;
                    else Debug.LogWarning("[ParameterPanelController] Sample " + (index + 1) + ": value " + code +
                                          " is not an option of '" + b.feature + "'.", this);
                }
            }

            sampleIndex = index;
            RefreshAllBindings();
            OnParametersChanged();
        }

        private void OnParametersChanged()
        {
            StopRun();
            if (liveUpdate) ShowInstant();
            else ResetView();
        }

        // ================================================================== running the tree

        /// <summary>Whole path at once (used for live updates).</summary>
        private void ShowInstant()
        {
            if (!TryPredict()) return;
            shown = current.Path.Count;
            if (highlighter != null) highlighter.ShowPath(current.Path, shown);
            UpdatePathText();
            UpdateResult();
        }

        /// <summary>Button: replays the path node by node.</summary>
        public void Run()
        {
            if (!initialized) return;
            StopRun();
            runRoutine = StartCoroutine(RunRoutine());
        }

        private IEnumerator RunRoutine()
        {
            if (!TryPredict())
            {
                runRoutine = null;
                yield break;
            }

            shown = 0;
            if (highlighter != null) highlighter.ShowPath(current.Path, 0);   // everything faded
            ShowPlaceholder();
            UpdatePathText();

            for (int i = 1; i <= current.Path.Count; i++)
            {
                yield return new WaitForSeconds(secondsPerStep);
                shown = i;
                if (highlighter != null) highlighter.ShowPath(current.Path, shown);
                UpdatePathText();
            }

            UpdateResult();
            runRoutine = null;
        }

        /// <summary>Button: lights one more node each time it is pressed.</summary>
        public void Step()
        {
            if (!initialized) return;
            StopRun();

            if (current == null || shown >= current.Path.Count)
            {
                if (!TryPredict()) return;
                shown = 0;
            }

            shown++;
            if (highlighter != null) highlighter.ShowPath(current.Path, shown);
            UpdatePathText();
            if (shown >= current.Path.Count) UpdateResult();
            else ShowPlaceholder();
        }

        /// <summary>Button: back to the normal, un-highlighted tree.</summary>
        public void ResetView()
        {
            StopRun();
            current = null;
            shown = 0;
            if (highlighter != null) highlighter.Clear();
            if (pathText != null) pathText.text = "";
            ShowPlaceholder();
        }

        private void StopRun()
        {
            if (runRoutine != null)
            {
                StopCoroutine(runRoutine);
                runRoutine = null;
            }
        }

        private bool TryPredict()
        {
            current = TreeInference.Predict(model.Root, values);
            if (!current.Ok)
            {
                Debug.LogError("[ParameterPanelController] " + current.Error, this);
                current = null;
                return false;
            }
            return true;
        }

        // ================================================================== result box

        private void ShowPlaceholder()
        {
            if (resultTitle != null)
            {
                resultTitle.text = placeholderTitle;
                resultTitle.color = placeholderColor;
            }
            if (resultBody != null) resultBody.text = "";
        }

        private void UpdateResult()
        {
            if (current == null) return;

            TreeNode leaf = current.Leaf;
            bool disease = leaf.Prediction == 1;

            if (resultTitle != null)
            {
                resultTitle.text = leaf.PredictionLabel.ToUpperInvariant();
                resultTitle.color = disease ? heartDiseaseColor : noDiseaseColor;
            }

            if (resultBody == null) return;

            var sb = new StringBuilder();
            if (leaf.ClassShare != null && leaf.ClassShare.Length >= 2)
            {
                double sum = leaf.ClassShare[0] + leaf.ClassShare[1];
                int heart = sum > 0.0 ? (int)Math.Round(leaf.ClassShare[1] / sum * 100.0) : 0;
                sb.Append(ClassName(1)).Append(' ').Append(heart).Append("%\n");
                sb.Append(ClassName(0)).Append(' ').Append(100 - heart).Append("%\n");
            }
            sb.Append("Leaf node ").Append(leaf.Id).Append(", built from ").Append(leaf.Samples).Append(" training samples.");
            if (leaf.IsTie)
                sb.Append("\nBoth classes are equally likely here, so the model defaults to ").Append(leaf.PredictionLabel).Append('.');
            if (sampleIndex >= 0 && sampleIndex < samples.Count)
            {
                bool right = samples[sampleIndex].TrueLabel == leaf.Prediction;
                sb.Append("\nDataset label: ").Append(ClassName(samples[sampleIndex].TrueLabel))
                  .Append(right ? " - prediction is correct." : " - prediction is wrong.");
            }
            resultBody.text = sb.ToString();
        }

        private void UpdatePathText()
        {
            if (pathText == null || current == null) return;

            var sb = new StringBuilder();
            for (int i = 0; i < current.Steps.Count && i < shown; i++)
            {
                TreeInference.Step step = current.Steps[i];
                TreeNode n = step.Node;
                string label = string.IsNullOrEmpty(n.FeatureLabel) ? n.Feature : n.FeatureLabel;
                sb.Append(i + 1).Append(". ").Append(label).Append(": ")
                  .Append(ValueName(n.Feature, step.Value))
                  .Append("  ->  ").Append(step.WentLeft ? "left" : "right").Append('\n');
            }
            if (shown >= current.Path.Count) sb.Append("Result: ").Append(current.Leaf.PredictionLabel);
            pathText.text = sb.ToString();
        }

        // ================================================================== small helpers

        private string ClassName(int cls)
        {
            return cls >= 0 && cls < className.Length ? className[cls] : "Class " + cls;
        }

        private string ValueName(string feature, double value)
        {
            if (bindings != null)
            {
                foreach (Binding b in bindings)
                {
                    if (b.feature != feature || b.options == null) continue;
                    foreach (FeatureOption o in b.options)
                    {
                        if (o.Code == (int)Math.Round(value)) return o.Name;
                    }
                }
            }
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private void RefreshAllBindings()
        {
            if (bindings == null) return;
            foreach (Binding b in bindings) RefreshBinding(b);
        }

        private void RefreshBinding(Binding b)
        {
            double v;
            if (b.options == null || !values.TryGetValue(b.feature, out v)) return;

            int code = (int)Math.Round(v);
            int index = b.options.FindIndex(o => o.Code == code);
            if (index < 0) return;

            if (b.dropdown != null)
            {
                b.dropdown.SetValueWithoutNotify(index);
                b.dropdown.RefreshShownValue();
            }
            else if (b.buttons != null)
            {
                for (int i = 0; i < b.buttons.Length; i++)
                {
                    Graphic g = b.buttons[i].targetGraphic;
                    if (g != null) g.color = i == index ? buttonSelectedColor : buttonNormalColor;
                }
            }
        }
    }
}