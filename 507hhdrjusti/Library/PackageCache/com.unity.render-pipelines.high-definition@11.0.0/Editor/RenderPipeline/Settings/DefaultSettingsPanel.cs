using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using UnityEditorInternal;
using System.Linq;

namespace UnityEditor.Rendering.HighDefinition
{
    class DefaultSettingsPanelProvider
    {
        static DefaultSettingsPanelIMGUI s_IMGUIImpl = new DefaultSettingsPanelIMGUI();

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new SettingsProvider("Project/HDRP Default Settings", SettingsScope.Project)
            {
                activateHandler = s_IMGUIImpl.OnActivate,
                keywords = SettingsProvider.GetSearchKeywordsFromGUIContentProperties<HDRenderPipelineUI.Styles.GeneralSection>()
                    .Concat(SettingsProvider.GetSearchKeywordsFromGUIContentProperties<DefaultSettingsPanelIMGUI.Styles>())
                    .Concat(OverridableFrameSettingsArea.frameSettingsKeywords).ToArray(),
                guiHandler = s_IMGUIImpl.DoGUI,
            };
        }

        class DefaultSettingsPanelIMGUI
        {
            // A wrapper for CoreEditorDrawers
            class CoreEditorDrawerEditorWrapper : Editor, IDefaultFrameSettingsType
            {
                public FrameSettingsRenderType GetFrameSettingsType()
                {
                    switch (HDRenderPipelineUI.selectedFrameSettings)
                    {
                        case HDRenderPipelineUI.SelectedFrameSettings.Camera: return FrameSettingsRenderType.Camera;
                        case HDRenderPipelineUI.SelectedFrameSettings.RealtimeReflection: return FrameSettingsRenderType.RealtimeReflection;
                        case HDRenderPipelineUI.SelectedFrameSettings.BakedOrCustomReflection: return FrameSettingsRenderType.CustomOrBakedReflection;
                    }
                    throw new Exception("unreachable");
                }
            }

            public class Styles
            {
                public const int labelWidth = 220;
                public static GUIContent defaultHDRPAsset = new GUIContent("Asset with the default settings");
                public static GUIContent defaultVolumeProfileLabel = new GUIContent("Default Volume Profile Asset");
                public static GUIContent lookDevVolumeProfileLabel = new GUIContent("LookDev Volume Profile Asset");
                public static GUIContent frameSettingsLabel = new GUIContent("Frame Settings");
                public static GUIContent volumeComponentsLabel = new GUIContent("Volume Components");
                public static GUIContent customPostProcessOrderLabel = new GUIContent("Custom Post Process Orders");
                public static GUIContent diffusionProfileSettingsLabel = new GUIContent("Default Diffusion Profile Assets");
            }

            Vector2 m_ScrollViewPosition = Vector2.zero;
            Editor m_CachedDefaultVolumeProfileEditor;
            Editor m_CachedLookDevVolumeProfileEditor;
            ReorderableList m_BeforeTransparentCustomPostProcesses;
            ReorderableList m_BeforeTAACustomPostProcesses;
            ReorderableList m_BeforePostProcessCustomPostProcesses;
            ReorderableList m_AfterPostProcessCustomPostProcesses;
            int m_CurrentVolumeProfileInstanceID;
            private Editor m_Cache;
            DiffusionProfileSettingsListUI m_DiffusionProfileUI;
            SerializedHDRenderPipelineAsset m_SerializeHDRPAsset;

            public void DoGUI(string searchContext)
            {
                if (HDRenderPipeline.defaultAsset == null)
                {
                    EditorGUILayout.HelpBox("Base SRP Asset is not a HDRenderPipelineAsset.", MessageType.Warning);
                    return;
                }

                m_SerializeHDRPAsset.Update();

                m_ScrollViewPosition = GUILayout.BeginScrollView(m_ScrollViewPosition, EditorStyles.largeLabel);
                Draw_GeneralSettings();
                EditorGUILayout.Space();

                EditorGUILayout.LabelField(Styles.frameSettingsLabel, EditorStyles.largeLabel);
                Draw_DefaultFrameSettings();
                EditorGUILayout.Space();

                EditorGUILayout.LabelField(Styles.volumeComponentsLabel, EditorStyles.largeLabel);
                Draw_VolumeInspector();
                EditorGUILayout.Space();

                EditorGUILayout.LabelField(Styles.customPostProcessOrderLabel, EditorStyles.largeLabel);
                Draw_CustomPostProcess();

                EditorGUILayout.LabelField(Styles.diffusionProfileSettingsLabel, EditorStyles.largeLabel);
                Draw_DiffusionProfileSettings();
                GUILayout.EndScrollView();
            }

            /// <summary>
            /// Executed when activate is called from the settings provider.
            /// </summary>
            /// <param name="searchContext"></param>
            /// <param name="rootElement"></param>
            public void OnActivate(string searchContext, VisualElement rootElement)
            {
                if (HDRenderPipeline.defaultAsset == null)
                    return;

                m_ScrollViewPosition = Vector2.zero;
                InitializeCustomPostProcessesLists();

                m_DiffusionProfileUI = new DiffusionProfileSettingsListUI()
                {
                    drawElement = DrawDiffusionProfileElement
                };

                var serializedObject = new SerializedObject(HDRenderPipeline.defaultAsset);
                m_SerializeHDRPAsset = new SerializedHDRenderPipelineAsset(serializedObject);

                var editorResources = HDRenderPipeline.defaultAsset.renderPipelineEditorResources;
                if (!EditorUtility.IsPersistent(editorResources))
                {
                    var editorResourcesPath = HDUtils.GetHDRenderPipelinePath() + "Editor/RenderPipelineResources/HDRenderPipelineEditorResources.asset";
                    HDRenderPipeline.defaultAsset.renderPipelineEditorResources = AssetDatabase.LoadAssetAtPath<HDRenderPipelineEditorResources>(editorResourcesPath);
                }
            }

            void InitializeCustomPostProcessesLists()
            {
                var hdrpAsset = HDRenderPipeline.defaultAsset;
                if (hdrpAsset == null)
                    return;

                var ppVolumeTypes = TypeCache.GetTypesDerivedFrom<CustomPostProcessVolumeComponent>();
                var ppVolumeTypeInjectionPoints = new Dictionary<Type, CustomPostProcessInjectionPoint>();
                foreach (var ppVolumeType in ppVolumeTypes)
                {
                    if (ppVolumeType.IsAbstract)
                        continue;

                    var comp = ScriptableObject.CreateInstance(ppVolumeType) as CustomPostProcessVolumeComponent;
                    ppVolumeTypeInjectionPoints[ppVolumeType] = comp.injectionPoint;
                    CoreUtils.Destroy(comp);
                }

                InitList(ref m_BeforeTransparentCustomPostProcesses, hdrpAsset.beforeTransparentCustomPostProcesses, "After Opaque And Sky", CustomPostProcessInjectionPoint.AfterOpaqueAndSky);
                InitList(ref m_BeforeTAACustomPostProcesses, hdrpAsset.beforeTAACustomPostProcesses, "Before TAA", CustomPostProcessInjectionPoint.BeforeTAA);
                InitList(ref m_BeforePostProcessCustomPostProcesses, hdrpAsset.beforePostProcessCustomPostProcesses, "Before Post Process", CustomPostProcessInjectionPoint.BeforePostProcess);
                InitList(ref m_AfterPostProcessCustomPostProcesses, hdrpAsset.afterPostProcessCustomPostProcesses, "After Post Process", CustomPostProcessInjectionPoint.AfterPostProcess);

                void InitList(ref ReorderableList reorderableList, List<string> customPostProcessTypes, string headerName, CustomPostProcessInjectionPoint injectionPoint)
                {
                    // Sanitize the list:
                    customPostProcessTypes.RemoveAll(s => Type.GetType(s) == null);

                    reorderableList = new ReorderableList(customPostProcessTypes, typeof(string));
                    reorderableList.drawHeaderCallback = (rect) =>
                        EditorGUI.LabelField(rect, headerName, EditorStyles.boldLabel);
                    reorderableList.drawElementCallback = (rect, index, isActive, isFocused) =>
                    {
                        rect.height = EditorGUIUtility.singleLineHeight;
                        var elemType = Type.GetType(customPostProcessTypes[index]);
                        EditorGUI.LabelField(rect, elemType.ToString(), EditorStyles.boldLabel);
                    };
                    reorderableList.onAddCallback = (list) =>
                    {
                        var menu = new GenericMenu();

                        foreach (var kp in ppVolumeTypeInjectionPoints)
                        {
                            if (kp.Value == injectionPoint && !customPostProcessTypes.Contains(kp.Key.AssemblyQualifiedName))
                                menu.AddItem(new GUIContent(kp.Key.ToString()), false, () =>
                                {
                                    Undo.RegisterCompleteObjectUndo(hdrpAsset, $"Added {kp.Key.ToString()} Custom Post Process");
                                    customPostProcessTypes.Add(kp.Key.AssemblyQualifiedName);
                                });
                        }

                        if (menu.GetItemCount() == 0)
                            menu.AddDisabledItem(new GUIContent("No Custom Post Process Availble"));

                        menu.ShowAsContext();
                        EditorUtility.SetDirty(hdrpAsset);
                    };
                    reorderableList.onRemoveCallback = (list) =>
                    {
                        Undo.RegisterCompleteObjectUndo(hdrpAsset, $"Removed {list.list[list.index].ToString()} Custom Post Process");
                        customPostProcessTypes.RemoveAt(list.index);
                        EditorUtility.SetDirty(hdrpAsset);
                    };
                    reorderableList.elementHeightCallback = _ => EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
                    reorderableList.onReorderCallback = (list) => EditorUtility.SetDirty(hdrpAsset);
                }
            }

            void Draw_CustomPostProcess()
            {
                var hdrpAsset = HDRenderPipeline.defaultAsset;
                if (hdrpAsset == null)
                    return;

                m_BeforeTransparentCustomPostProcesses.DoLayoutList();
                m_BeforeTAACustomPostProcesses.DoLayoutList();
                m_BeforePostProcessCustomPostProcesses.DoLayoutList();
                m_AfterPostProcessCustomPostProcesses.DoLayoutList();
            }

            void Draw_GeneralSettings()
            {
                var hdrpAsset = HDRenderPipeline.defaultAsset;
                if (hdrpAsset == null)
                {
                    EditorGUILayout.HelpBox("Base SRP Asset is not a HDRenderPipelineAsset.", MessageType.Warning);
                    return;
                }

                var oldWidth = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = Styles.labelWidth;

                GUI.enabled = false;
                EditorGUILayout.ObjectField(Styles.defaultHDRPAsset, hdrpAsset, typeof(HDRenderPipelineAsset), false);
                GUI.enabled = true;

                HDRenderPipelineUI.GeneralSection.Draw(m_SerializeHDRPAsset, null);

                m_SerializeHDRPAsset.serializedObject.ApplyModifiedProperties();
                EditorGUIUtility.labelWidth = oldWidth;
            }

            void Draw_VolumeInspector()
            {
                var hdrpAsset = HDRenderPipeline.defaultAsset;
                if (hdrpAsset == null)
                    return;

                var oldWidth = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = Styles.labelWidth;

                EditorGUILayout.BeginHorizontal();
                var asset = EditorDefaultSettings.GetOrAssignDefaultVolumeProfile();
                var newAsset = (VolumeProfile)EditorGUILayout.ObjectField(Styles.defaultVolumeProfileLabel, asset, typeof(VolumeProfile), false);
                if (newAsset == null)
                {
                    Debug.Log("Default Volume Profile Asset cannot be null. Rolling back to previous value.");
                }
                else if (newAsset != asset)
                {
                    Undo.RegisterCompleteObjectUndo(hdrpAsset, "Default Volume Profile Asset Change.");
                    asset = newAsset;
                    hdrpAsset.defaultVolumeProfile = asset;
                    EditorUtility.SetDirty(hdrpAsset);
                }

                if (GUILayout.Button(EditorGUIUtility.TrTextContent("New", "Create a new Volume Profile for default in your default resource folder (defined in Wizard)"), GUILayout.Width(38), GUILayout.Height(18)))
                {
                    DefaultVolumeProfileCreator.CreateAndAssign(DefaultVolumeProfileCreator.Kind.Default);
                }
                EditorGUILayout.EndHorizontal();

                // The state of the profile can change without the asset reference changing so in this case we need to reset the editor.
                if (m_CurrentVolumeProfileInstanceID != asset.GetInstanceID() && m_CachedDefaultVolumeProfileEditor != null)
                {
                    m_CurrentVolumeProfileInstanceID = asset.GetInstanceID();
                    m_CachedDefaultVolumeProfileEditor = null;
                }

                Editor.CreateCachedEditor(asset, Type.GetType("UnityEditor.Rendering.VolumeProfileEditor"), ref m_CachedDefaultVolumeProfileEditor);
                EditorGUIUtility.labelWidth -= 18;
                bool oldEnabled = GUI.enabled;
                GUI.enabled = AssetDatabase.IsOpenForEdit(asset);
                m_CachedDefaultVolumeProfileEditor.OnInspectorGUI();
                GUI.enabled = oldEnabled;
                EditorGUIUtility.labelWidth = oldWidth;

                EditorGUILayout.Space();

                EditorGUILayout.BeginHorizontal();
                var lookDevAsset = EditorDefaultSettings.GetOrAssignLookDevVolumeProfile();
                EditorGUIUtility.labelWidth = 221;
                var newLookDevAsset = (VolumeProfile)EditorGUILayout.ObjectField(Styles.lookDevVolumeProfileLabel, lookDevAsset, typeof(VolumeProfile), false);
                if (lookDevAsset == null)
                {
                    Debug.Log("LookDev Volume Profile Asset cannot be null. Rolling back to previous value.");
                }
                else if (newLookDevAsset != lookDevAsset)
                {
                    hdrpAsset.defaultLookDevProfile = newLookDevAsset;
                    EditorUtility.SetDirty(hdrpAsset);
                }

                if (GUILayout.Button(EditorGUIUtility.TrTextContent("New", "Create a new Volume Profile for default in your default resource folder (defined in Wizard)"), GUILayout.Width(38), GUILayout.Height(18)))
                {
                    DefaultVolumeProfileCreator.CreateAndAssign(DefaultVolumeProfileCreator.Kind.LookDev);
                }
                EditorGUILayout.EndHorizontal();

                Editor.CreateCachedEditor(lookDevAsset, Type.GetType("UnityEditor.Rendering.VolumeProfileEditor"), ref m_CachedLookDevVolumeProfileEditor);
                EditorGUIUtility.labelWidth -= 18;
                oldEnabled = GUI.enabled;
                GUI.enabled = AssetDatabase.IsOpenForEdit(asset);
                m_CachedLookDevVolumeProfileEditor.OnInspectorGUI();
                GUI.enabled = oldEnabled;
                EditorGUIUtility.labelWidth = oldWidth;

                if (lookDevAsset.Has<VisualEnvironment>())
                    EditorGUILayout.HelpBox("VisualEnvironment is not modifiable and will be overridden by the LookDev", MessageType.Warning);
                if (lookDevAsset.Has<HDRISky>())
                    EditorGUILayout.HelpBox("HDRISky is not modifiable and will be overridden by the LookDev", MessageType.Warning);
            }

            void Draw_DefaultFrameSettings()
            {
                var hdrpAsset = HDRenderPipeline.defaultAsset;
                if (hdrpAsset == null)
                    return;

                Editor.CreateCachedEditor(hdrpAsset, typeof(CoreEditorDrawerEditorWrapper), ref m_Cache);

                HDRenderPipelineUI.FrameSettingsSection.Draw(m_SerializeHDRPAsset, m_Cache);
                m_SerializeHDRPAsset.serializedObject.ApplyModifiedProperties();
            }

            void DrawDiffusionProfileElement(SerializedProperty element, Rect rect, int index)
            {
                EditorGUI.BeginChangeCheck();
                EditorGUI.ObjectField(rect, element, EditorGUIUtility.TrTextContent("Profile " + index));
                if (EditorGUI.EndChangeCheck())
                    m_SerializeHDRPAsset.serializedObject.ApplyModifiedProperties();
            }

            void Draw_DiffusionProfileSettings()
            {
                m_DiffusionProfileUI.OnGUI(m_SerializeHDRPAsset.diffusionProfileSettingsList);
            }
        }
    }

    class DefaultVolumeProfileCreator : ProjectWindowCallback.EndNameEditAction
    {
        public enum Kind { Default, LookDev }
        Kind m_Kind;

        void SetKind(Kind kind) => m_Kind = kind;

        public override void Action(int instanceId, string pathName, string resourceFile)
        {
            var profile = VolumeProfileFactory.CreateVolumeProfileAtPath(pathName);
            ProjectWindowUtil.ShowCreatedAsset(profile);
            Assign(profile);
        }

        void Assign(VolumeProfile profile)
        {
            var hdrpAsset = HDRenderPipeline.defaultAsset;
            switch (m_Kind)
            {
                case Kind.Default:
                    hdrpAsset.defaultVolumeProfile = profile;
                    break;
                case Kind.LookDev:
                    hdrpAsset.defaultLookDevProfile = profile;
                    break;
            }
        }

        static string GetDefaultName(Kind kind)
        {
            string defaultName;
            switch (kind)
            {
                case Kind.Default:
                    defaultName = "DefaultVolumeSettingsProfile";
                    break;
                case Kind.LookDev:
                    defaultName = "LookDevVolumeSettingsProfile";
                    break;
                default:
                    defaultName = "N/A";
                    break;
            }
            return defaultName;
        }

        public static void CreateAndAssign(Kind kind)
        {
            var assetCreator = ScriptableObject.CreateInstance<DefaultVolumeProfileCreator>();
            assetCreator.SetKind(kind);
            ProjectWindowUtil.StartNameEditingIfProjectWindowExists(assetCreator.GetInstanceID(), assetCreator, $"Assets/{HDProjectSettings.projectSettingsFolderPath}/{GetDefaultName(kind)}.asset", null, null);
        }
    }
}
