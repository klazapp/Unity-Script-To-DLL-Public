using UnityEngine;
using UnityEditor;
using System.CodeDom.Compiler;
using Microsoft.CSharp;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.RegularExpressions;

//TODO: When package is imported, automatically adjust api compatibility level from .netstandard to .netframework for all build platforms (android, ios, standalone)
public class ScriptToDLLConverterWindow : EditorWindow
{
    private List<string> _referenceDlls = new List<string>();
    private List<string> _scriptsToCompile = new List<string>();
    private string _customSymbols = "";
    private string _outputDllName = "CompiledScripts.dll";
    private List<string> _foldersSelected = new List<string>();
    private HashSet<string> _foundSymbols = new HashSet<string>();

    [MenuItem("Klazapp/Tool/Script to DLL Converter")]
    public static void ShowWindow()
    {
        var inspectorWindowType = typeof(Editor).Assembly.GetType("UnityEditor.InspectorWindow");
        // Find an existing open window or if none, make a new one:
        var window = GetWindow<ScriptToDLLConverterWindow>("Script to DLL Converter", inspectorWindowType);
        window.AddNetStandardReference();
    }

    private void AddNetStandardReference()
    {
        var script = MonoScript.FromScriptableObject(this);
        var scriptPath = AssetDatabase.GetAssetPath(script);
        var scriptDirectory = Path.GetDirectoryName(scriptPath);

        var netStandardDllPath = Path.Combine(scriptDirectory, "netstandard.dll");
        if (File.Exists(netStandardDllPath) && !_referenceDlls.Contains(netStandardDllPath))
        {
            _referenceDlls.Add(netStandardDllPath);
            Debug.Log("netstandard.dll found and added as reference.");
        }
        else
        {
            Debug.LogWarning("netstandard.dll not found in the script's directory.");
        }
    }
    
    private void OnGUI()
    {
        GUILayout.Label("Convert Scripts to DLL", EditorStyles.boldLabel);

        // Reference DLLs Section
        if (GUILayout.Button("Add Reference DLL"))
        {
            var path = EditorUtility.OpenFilePanel("Select Reference DLL", "", "dll");
            if (!string.IsNullOrEmpty(path) && !_referenceDlls.Contains(path))
            {
                _referenceDlls.Add(path);
            }
        }

        DisplayListWithRemoveButtons(_referenceDlls, "Remove");

        // Scripts Section
        if (GUILayout.Button("Add Scripts from Folder"))
        {
            var folderPath = EditorUtility.OpenFolderPanel("Select Folder with Scripts", "", "");
            if (!string.IsNullOrEmpty(folderPath) && !_foldersSelected.Contains(folderPath))
            {
                _foldersSelected.Add(folderPath);
                AddScriptsFromFolder(folderPath);
            }
        }

        DisplayListWithRemoveButtons(_foldersSelected, "Remove Folder");

        DisplayListWithRemoveButtons(_scriptsToCompile, "Remove Script");

        // Custom Symbols and Output DLL Name Section
        GUILayout.Label("Custom Compilation Symbols (detected and added automatically):", EditorStyles.boldLabel);
        _customSymbols = EditorGUILayout.TextField(_customSymbols);

        GUILayout.Label("Output DLL Name:", EditorStyles.boldLabel);
        _outputDllName = EditorGUILayout.TextField(_outputDllName);

        if (GUILayout.Button("Scan for Define Symbols & Convert"))
        {
            ScanForDefineSymbols();
            CompileScriptsToDLL();
        }
    }

    private void AddScriptsFromFolder(string folderPath)
    {
        var scriptFiles = Directory.GetFiles(folderPath, "*.cs", SearchOption.AllDirectories);
        foreach (var file in scriptFiles)
        {
            if (!_scriptsToCompile.Contains(file))
            {
                _scriptsToCompile.Add(file);
            }
        }
    }

    private void DisplayListWithRemoveButtons(List<string> list, string buttonText)
    {
        for (int i = 0; i < list.Count; i++)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(list[i]);
            if (GUILayout.Button(buttonText))
            {
                list.RemoveAt(i);
                break; // Exit the loop to avoid modifying the collection while iterating
            }
            GUILayout.EndHorizontal();
        }
    }

    private void ScanForDefineSymbols()
    {
        _foundSymbols.Clear();

        foreach (var script in _scriptsToCompile)
        {
            string[] lines = File.ReadAllLines(script);
            foreach (var line in lines)
            {
                if (line.TrimStart().StartsWith("#define"))
                {
                    var match = Regex.Match(line, @"#define\s+(\w+)");
                    if (match.Success)
                    {
                        _foundSymbols.Add(match.Groups[1].Value);
                    }
                }
                else if (line.TrimStart().StartsWith("#if") || line.TrimStart().StartsWith("#elif"))
                {
                    var matches = Regex.Matches(line, @"\b\w+\b");
                    foreach (Match match in matches)
                    {
                        if (!IsCommonKeyword(match.Value))
                        {
                            _foundSymbols.Add(match.Value);
                        }
                    }
                }
                else if (line.Contains("[Conditional("))
                {
                    var match = Regex.Match(line, @"\[Conditional\(\""(.+?)\""\)\]");
                    if (match.Success)
                    {
                        _foundSymbols.Add(match.Groups[1].Value);
                    }
                }
            }
        }

        _customSymbols = string.Join(";", _foundSymbols.ToArray());
        Debug.Log("Custom Scripting Define Symbols Detected: " + _customSymbols);
    }

    private bool IsCommonKeyword(string word)
    {
        string[] commonKeywords = { "if", "elif", "else", "true", "false", "endif", "defined" };
        return commonKeywords.Contains(word);
    }


    private void CompileScriptsToDLL()
    {
        CodeDomProvider codeProvider = new CSharpCodeProvider();
        CompilerParameters parameters = new CompilerParameters
        {
            GenerateExecutable = false,
            OutputAssembly = Path.Combine(Application.dataPath, _outputDllName)
        };

        foreach (var dll in _referenceDlls)
        {
            parameters.ReferencedAssemblies.Add(dll);
        }

        if (!string.IsNullOrEmpty(_customSymbols))
        {
            parameters.CompilerOptions = $"/define:{_customSymbols}";
        }

        CompilerResults results = codeProvider.CompileAssemblyFromFile(parameters, _scriptsToCompile.ToArray());

        if (results.Errors.HasErrors)
        {
            string errors = string.Join("\n", results.Errors.Cast<CompilerError>().Select(error => error.ToString()));
            Debug.LogError(errors);
        }
        else
        {
            Debug.Log("DLL compiled successfully: " + parameters.OutputAssembly);
            AssetDatabase.Refresh(); // Refresh the Asset Database to show the new DLL in Unity Editor
        }
    }
}
