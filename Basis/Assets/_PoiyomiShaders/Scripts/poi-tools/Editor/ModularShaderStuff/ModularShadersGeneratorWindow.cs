using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions; // Added for Regex
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using UnityEngine.UIElements;
using Poiyomi.ModularShaderSystem;

#if UNITY_2019_4
using UnityEditor.Experimental.SceneManagement;
#endif

namespace Poi.Tools
{
    public class ModularShadersGeneratorElement : VisualElement
    {
        private bool _isSelected;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isErrored) return;
                _isSelected = value;
                _toggle.SetValueWithoutNotify(_isSelected);
            }
        }

        public ModularShader Shader { get; set; }

        private readonly Toggle _toggle;
        private readonly bool _isErrored;
        public ModularShadersGeneratorElement(ModularShader shader)
        {
            Shader = shader;
            style.flexDirection = FlexDirection.Row;
            _toggle = new Toggle();
            _toggle.RegisterValueChangedCallback(evt => IsSelected = evt.newValue);
            Add(_toggle);
            var label = new Label(Shader.Name);
            label.style.flexGrow = 1;
            Add(label);
            var shaderObject = new UnityEditor.UIElements.ObjectField();
            shaderObject.objectType = typeof(ModularShader);
            shaderObject.value = shader;
            shaderObject.style.minWidth = new StyleLength(new Length(50f, LengthUnit.Percent));
            shaderObject.style.maxWidth = new StyleLength(new Length(50f, LengthUnit.Percent));
            Add(shaderObject);
            var issues = ShaderGenerator.CheckShaderIssues(shader);
            if (issues.Count > 0)
            {
                _isErrored = true;
                _toggle.SetEnabled(false);
                VisualElement element = new VisualElement();
                element.AddToClassList("error");
                element.tooltip = "Modular shader has the following errors: \n -" + string.Join("\n -", issues);
                Add(element);
            }
        }
    }

    [Serializable]
    public class ModularShadersGeneratorWindow : EditorWindow
    {
        [MenuItem("Poi/Modular Shaders Generator")]
        private static void ShowWindow()
        {
            var window = GetWindow<ModularShadersGeneratorWindow>();
            window.titleContent = new GUIContent("Modular Shaders Generator");
            window.Show();
        }

        private VisualElement _root;
        internal List<ModularShadersGeneratorElement> _elements;

        private void OnDestroy()
        {
            UnregisterCallbacks();
        }

        private void CreateGUI()
        {
            _root = rootVisualElement;
            Reload();
        }

        private void Reload()
        {
            _root.Clear();

            var styleSheet = Resources.Load<StyleSheet>("Poi/ModularShadersGeneratorStyle");
            _root.styleSheets.Add(styleSheet);

            var view = new ScrollView(ScrollViewMode.Vertical);
#if UNITY_2021_2_OR_NEWER
            view.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
#else
            view.horizontalScroller.visible = false;
#endif

            var selectButtons = new VisualElement();
            selectButtons.AddToClassList("buttons-area");

            var selectAll = new Button();
            selectAll.text = "Select all";
            selectAll.style.flexGrow = 1;
            selectAll.clicked += () =>
            {
                foreach (var element in _elements)
                    element.IsSelected = true;
            };
            selectButtons.Add(selectAll);

            var deselectAll = new Button();
            deselectAll.text = "Deselect all";
            deselectAll.style.flexGrow = 1;
            deselectAll.clicked += () =>
            {
                foreach (var element in _elements)
                    element.IsSelected = false;
            };
            selectButtons.Add(deselectAll);

            var toggleSelections = new Button();
            toggleSelections.text = "Toggle selections";
            toggleSelections.style.flexGrow = 1;
            toggleSelections.clicked += () =>
            {
                foreach (var element in _elements)
                    element.IsSelected = !element.IsSelected;
            };
            selectButtons.Add(toggleSelections);

            var reloadButton = new Button();
            reloadButton.text = "Refresh";
            reloadButton.style.flexGrow = 1;
            reloadButton.clicked += () =>
            {
                AssetDatabase.Refresh();
                Reload();
            };
            selectButtons.Add(reloadButton);

            view.Add(selectButtons);

            var typeButtons = new VisualElement();
            typeButtons.AddToClassList("buttons-area");

            var selectToon = new Button { text = "Toon" };
            selectToon.style.flexGrow = 1;
            selectToon.clicked += () => SelectByName("Toon", excludeUrp: true);
            typeButtons.Add(selectToon);

            var selectPro = new Button { text = "Pro" };
            selectPro.style.flexGrow = 1;
            selectPro.clicked += () => SelectByName("Pro", excludeUrp: true);
            typeButtons.Add(selectPro);

            var selectToonUrp = new Button { text = "Toon URP" };
            selectToonUrp.style.flexGrow = 1;
            selectToonUrp.clicked += () => SelectByName("Toon", requireUrp: true);
            typeButtons.Add(selectToonUrp);

            var selectProUrp = new Button { text = "Pro URP" };
            selectProUrp.style.flexGrow = 1;
            selectProUrp.clicked += () => SelectByName("Pro", requireUrp: true);
            typeButtons.Add(selectProUrp);

            view.Add(typeButtons);

            // Load all modular shaders
            _elements = new List<ModularShadersGeneratorElement>();
            foreach (var modularShader in FindAssetsByType<ModularShader>())
            {
                var element = new ModularShadersGeneratorElement(modularShader);
                _elements.Add(element);
                view.Add(element);
            }

            var generateButton = new Button();
            generateButton.style.marginLeft = 6;
            generateButton.style.marginRight = 8;
            generateButton.text = "Generate Shaders";
            generateButton.clicked += GenerateShaders;

            VisualElement destinationsList = SetupDestinationsListView();

            view.Add(destinationsList);
            _root.Add(view);
            view.Add(generateButton);

            UnregisterCallbacks();
            RegisterCallbacks();
        }

        VisualElement SetupDestinationsListView()
        {
#if UNITY_2021_2_OR_NEWER
            ListView destinationsList = new ListView()
            {
                headerTitle = "Destinations",
                reorderable = true,
                showAddRemoveFooter = true,
                reorderMode = ListViewReorderMode.Animated,
                itemsSource = ShaderDestinationManager.Instance.destinations,
                fixedItemHeight = 80, // Increased height for the new field
                showBorder = true,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
                makeItem = () => new ShaderDestinationListElement(),
                bindItem = (elem, index) =>
                {
                    (elem as ShaderDestinationListElement).BindListItem(ShaderDestinationManager.Instance.destinations[index]);
                },
                unbindItem = (elem, index) =>
                {
                    (elem as ShaderDestinationListElement).UnbindListItem();
                }
            };
#else
            VisualElement destinationsList = new IMGUIContainer(() =>
            {
                EditorGUILayout.HelpBox("Editing the destinations list is only supported in newer versions of Unity. Shaders will go to:\nAssets/_PoiyomiShaders/Shaders/9.0/Toon\nAssets/_PoiyomiShaders/Shaders/9.0/Pro", MessageType.Warning);
            });
#endif
            destinationsList.style.marginLeft = 6;
            destinationsList.style.marginRight = 8;
            destinationsList.style.marginTop = 10;

            return destinationsList;
        }

        void UnregisterCallbacks()
        {
            EditorApplication.playModeStateChanged -= HandlePlayModeStateChange;
            EditorSceneManager.sceneOpened -= HandleSceneOpened;
            EditorSceneManager.newSceneCreated -= HandleNewScene;
#if UNITY_2021_2_OR_NEWER
            PrefabStage.prefabStageOpened -= HandlePrefabSceneOpenOrClose;
            PrefabStage.prefabStageClosing -= HandlePrefabSceneOpenOrClose;
#endif
        }

        void RegisterCallbacks()
        {
            EditorApplication.playModeStateChanged += HandlePlayModeStateChange;
            EditorSceneManager.sceneOpened += HandleSceneOpened;
            EditorSceneManager.newSceneCreated += HandleNewScene;
#if UNITY_2021_2_OR_NEWER
            PrefabStage.prefabStageOpened += HandlePrefabSceneOpenOrClose;
            PrefabStage.prefabStageClosing += HandlePrefabSceneOpenOrClose;
#endif
        }

#if UNITY_2021_2_OR_NEWER
        void HandlePrefabSceneOpenOrClose(PrefabStage obj) => Reload();
#endif

        void HandleNewScene(Scene scene, NewSceneSetup setup, NewSceneMode mode)
        {
            if (mode == NewSceneMode.Single)
                Reload();
        }

        void HandleSceneOpened(Scene scene, OpenSceneMode mode)
        {
            if (mode == OpenSceneMode.Single)
                Reload();
        }

        void HandlePlayModeStateChange(PlayModeStateChange obj) => Reload();

        void SelectByName(string filter, bool excludeUrp = false, bool requireUrp = false)
        {
            foreach (var element in _elements)
            {
                bool matches = element.Shader.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
                bool hasUrp = element.Shader.Name.IndexOf("URP", StringComparison.OrdinalIgnoreCase) >= 0;
                if (excludeUrp && hasUrp) matches = false;
                if (requireUrp && !hasUrp) matches = false;
                element.IsSelected = matches;
            }
        }

        public static void ShowFolderSelector(TextField textField)
        {
            string path = textField.value;
            if (Directory.Exists(path))
            {
                path = Directory.GetParent(path).FullName;
            }
            else
            {
                path = "Assets";
            }
            path = EditorUtility.OpenFolderPanel("Select folder to use", path, "");
            if (path.Length == 0)
                return;

            if (!Directory.Exists(path))
            {
                EditorUtility.DisplayDialog("Error", "The folder does not exist", "Ok");
                return;
            }

            textField.value = path.Replace(Application.dataPath, "Assets");
        }

        internal void GenerateShaders()
        {
            var destinations = ShaderDestinationManager.Instance.destinations;
            var enabledDestinations = destinations.Where(dest => dest.enabled).ToArray();
            if (enabledDestinations.Length == 0)
            {
                Debug.LogError("Can't generate shaders if no destination folders are set");
                return;
            }

            // --- Find the template file path ---
            string[] templateGuids = AssetDatabase.FindAssets("VRLT_PropsUIBoilerPlate l:poiTemplate");
            if (templateGuids.Length == 0)
            {
                Debug.LogError("Could not find VRLT_PropsUIBoilerPlate.poiTemplate. Ensure the file has the 'poiTemplate' asset label.");
                return;
            }
            string templatePath = AssetDatabase.GUIDToAssetPath(templateGuids[0]);

            // --- Main Generation Loop ---
            foreach (ModularShadersGeneratorElement element in _elements.Where(x => x.IsSelected))
            {
                ShaderDestinationManager.ShaderDestination destinationRule = null;

                // Find the matching rule for the current shader element
                foreach (var rule in enabledDestinations)
                {
                    bool match = false;
                    switch (rule.matchType)
                    {
                        case ShaderDestinationManager.ShaderDestination.MatchType.Always: match = true; break;
                        case ShaderDestinationManager.ShaderDestination.MatchType.Contains: match = element.Shader.Name.IndexOf(rule.matchString, StringComparison.OrdinalIgnoreCase) >= 0; break;
                        case ShaderDestinationManager.ShaderDestination.MatchType.StartsWith: match = element.Shader.Name.StartsWith(rule.matchString, StringComparison.OrdinalIgnoreCase); break;
                        case ShaderDestinationManager.ShaderDestination.MatchType.EndsWith: match = element.Shader.Name.EndsWith(rule.matchString, StringComparison.OrdinalIgnoreCase); break;
                        case ShaderDestinationManager.ShaderDestination.MatchType.Equals: match = element.Shader.Name.Equals(rule.matchString, StringComparison.OrdinalIgnoreCase); break;
                        case ShaderDestinationManager.ShaderDestination.MatchType.Regex: match = Regex.IsMatch(element.Shader.Name, rule.matchString); break;
                    }
                    if (match)
                    {
                        destinationRule = rule;
                        break;
                    }
                }

                if (destinationRule == null)
                {
                    EditorUtility.DisplayDialog("Error", $"Couldn't match shader {element.Shader.Name} to any path.", "Ok");
                    continue;
                }

                // If an override exists for this rule, permanently change the template file
                if (!string.IsNullOrWhiteSpace(destinationRule.versionOverride))
                {
                    string templateContent = File.ReadAllText(templatePath);
                    string newLine = $"shader_master_label (\"<color=#E75898ff>Poiyomi {destinationRule.versionOverride}</color>\", Float) = 0";
                    string pattern = @"shader_master_label\s*\(\""<color=#E75898ff>Poiyomi\s*[\d\.]+<\/color>\"",\s*Float\)\s*=\s*0";
                    string newContent = Regex.Replace(templateContent, pattern, newLine);

                    // Write the change to disk
                    File.WriteAllText(templatePath, newContent);

                    // --- Add this line to force Unity to see the change immediately ---
                    AssetDatabase.ImportAsset(templatePath, ImportAssetOptions.ForceUpdate);

                    Debug.Log($"Template file permanently updated to version {destinationRule.versionOverride}.");
                }

                // Generate the shader using the updated template
                ShaderGenerator.GenerateShader(destinationRule.folderPath, element.Shader);
            }

            AssetDatabase.Refresh();
            Debug.Log("Finished generating shaders.");
            EditorApplication.delayCall += () =>
            {
                EditorApplication.delayCall += () =>
                {
                    Thry.ShaderEditor.ReloadActive();
                    InternalEditorUtility.RepaintAllViews();
                };
            };
        }

        private static T[] FindAssetsByType<T>() where T : UnityEngine.Object
        {
            List<T> assets = new List<T>();
            string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).ToString().Replace("UnityEngine.", "")}");
            for (int i = 0; i < guids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                T asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
                if (asset != null)
                    assets.Add(asset);
            }
            return assets.ToArray();
        }
    }
    public class ModularShadersAutoGen : AssetPostprocessor
    {
        private const string IsGeneratingKey = "Poi_IsGeneratingShaders";

#if UNITY_2021_2_OR_NEWER
    static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths, bool didDomainReload)
#else
        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
#endif
        {
            if (SessionState.GetBool(IsGeneratingKey, false))
                return;

            bool update = false;
            foreach (var importedAssetPath in importedAssets)
            {
                // --- Add this block to ignore the specific file ---
                string fileName = System.IO.Path.GetFileName(importedAssetPath);
                if (fileName.Equals("VRLT_PropsUIBoilerPlate.poiTemplate", System.StringComparison.OrdinalIgnoreCase))
                {
                    continue; // Skip this specific file
                }
                // --- End of new block ---

                string ext = System.IO.Path.GetExtension(importedAssetPath);
                if (ext == ".poiTemplateCollection" || ext == ".poiTemplate")
                {
                    update = true;
                    break;
                }
            }
            if (update)
            {
                var msgw = Resources.FindObjectsOfTypeAll<ModularShadersGeneratorWindow>().FirstOrDefault();
                if (msgw != null)
                {
                    if (msgw._elements != null && msgw._elements.Count(x => x.IsSelected) > 0)
                    {
                        EditorApplication.delayCall += () =>
                        {
                            SessionState.SetBool(IsGeneratingKey, true);
                            try
                            {
                                msgw.GenerateShaders();
                            }
                            finally
                            {
                                SessionState.SetBool(IsGeneratingKey, false);
                            }
                        };
                    }
                }
            }
        }
    }
}