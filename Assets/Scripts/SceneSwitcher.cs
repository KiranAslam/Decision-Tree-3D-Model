using UnityEngine;
using UnityEngine.Serialization;
using System;

public class SceneSwitcher : MonoBehaviour
{
    public static SceneSwitcher Instance { get; private set; }

    [Header("Panels")]
    public GameObject panelMainMenu;
    public GameObject panelKMC;

    [Header("DT Model")]
    public GameObject DTModel;

    [Header("Robot")]
    [Tooltip("Objects that should only be visible on the Main Menu.")]
    [FormerlySerializedAs("robotObjects")]
    public GameObject[] mainMenuObjects;

    [Header("3D Camera")]
    public IntegratedCameraController modelCamera;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        ShowMainMenu();
    }

    private void SetPanel(GameObject target)
    {
        if (panelMainMenu != null) panelMainMenu.SetActive(panelMainMenu == target);
        if (panelKMC != null) panelKMC.SetActive(panelKMC == target);
    }

    private void SetDTModel(bool isVisible)
    {
        if (DTModel == null) return;

        if (isVisible) DisableRuntimeGenerators();

        DTModel.SetActive(isVisible);
    }

    private void DisableRuntimeGenerators()
    {
        MonoBehaviour[] components = DTModel.GetComponentsInChildren<MonoBehaviour>(true);

        foreach (MonoBehaviour component in components)
        {
            if (component == null) continue;

            Type componentType = component.GetType();
            if (componentType.Name == "FinalLayerNormGenerator")
                continue;

            if (componentType.Name.EndsWith("Generator", StringComparison.Ordinal))
                component.enabled = false;
        }
    }

    private void SetMainMenuObjects(bool isVisible)
    {
        if (mainMenuObjects == null) return;
        foreach (GameObject go in mainMenuObjects)
            if (go != null) go.SetActive(isVisible);
    }

    public void ShowMainMenu()
    {
        SetPanel(panelMainMenu);
        SetDTModel(false);
        SetMainMenuObjects(true);
    }

    public void ShowDTModel()
    {
        SetPanel(panelKMC);
        SetDTModel(true);
        SetMainMenuObjects(false);

        if (modelCamera != null && DTModel != null)
        {
            Transform origin = DTModel.transform.Find("NetworkOrigin");
            modelCamera.targetFocus = origin != null ? origin : DTModel.transform;
            modelCamera.ResetToDefaultView();
        }
    }

    public void ShowLLMModel() => ShowDTModel();
    public void ShowModelSelection() => ShowDTModel();
    public void ShowDNNModel() => ShowDTModel();
}
