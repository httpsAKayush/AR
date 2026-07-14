using UnityEngine;
using UnityEngine.XR;
using System.Collections.Generic;

public class CanvasToggle : MonoBehaviour
{
    [Header("Canvas to toggle")]
    public GameObject canvas;

    [Header("Toggle Button")]
    public bool useLeftMenu = true;   // true = left Menu, false = right Meta button

    private InputDevice leftDevice;
    private InputDevice rightDevice;
    private bool buttonWasPressed = false;

    void Update()
    {
        if (!leftDevice.isValid)
            leftDevice = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        if (!rightDevice.isValid)
            rightDevice = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

        bool pressed = false;

        if (useLeftMenu)
            leftDevice.TryGetFeatureValue(CommonUsages.menuButton, out pressed);
        else
            rightDevice.TryGetFeatureValue(CommonUsages.primaryButton, out pressed);

        if (pressed && !buttonWasPressed)
        {
            canvas.SetActive(!canvas.activeSelf);
        }

        buttonWasPressed = pressed;
    }
}