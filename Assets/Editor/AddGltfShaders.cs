using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public class AddGltfShaders
{
    [MenuItem("Tools/Add GLTFast Shaders To Always Included")]
    static void AddShaders()
    {
        string[] shaderNames = {
            "glTF-pbrMetallicRoughness",
            "glTF-unlit",
            "glTF-pbrSpecularGlossiness"
        };

        var graphicsSettingsObj = new SerializedObject(GraphicsSettings.GetGraphicsSettings());
        var arrayProp = graphicsSettingsObj.FindProperty("m_AlwaysIncludedShaders");

        foreach (var name in shaderNames)
        {
            Shader shader = Shader.Find(name);
            if (shader == null)
            {
                Debug.LogWarning($"Shader not found: {name}");
                continue;
            }

            bool alreadyExists = false;
            for (int i = 0; i < arrayProp.arraySize; i++)
            {
                if (arrayProp.GetArrayElementAtIndex(i).objectReferenceValue == shader)
                {
                    alreadyExists = true;
                    break;
                }
            }

            if (!alreadyExists)
            {
                int index = arrayProp.arraySize;
                arrayProp.InsertArrayElementAtIndex(index);
                arrayProp.GetArrayElementAtIndex(index).objectReferenceValue = shader;
                Debug.Log($"Added shader: {name}");
            }
            else
            {
                Debug.Log($"Already present: {name}");
            }
        }

        graphicsSettingsObj.ApplyModifiedProperties();
    }
}