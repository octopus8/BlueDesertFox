// GENERATED AUTOMATICALLY FROM 'Assets/VRLogConsole/Scripts/Input/ConsoleInput.inputactions'

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;

public class @ConsoleInput : IInputActionCollection, IDisposable
{
    public InputActionAsset asset { get; }
    public @ConsoleInput()
    {
        asset = InputActionAsset.FromJson(@"{
    ""name"": ""ConsoleInput"",
    ""maps"": [
        {
            ""name"": ""XR Left Hand"",
            ""id"": ""494a540f-378d-4c7f-bbfd-f822fda16813"",
            ""actions"": [
                {
                    ""name"": ""Position"",
                    ""type"": ""Value"",
                    ""id"": ""efb47b5b-5d11-47cf-b470-a447193929c2"",
                    ""expectedControlType"": ""Vector3"",
                    ""processors"": """",
                    ""interactions"": """"
                },
                {
                    ""name"": ""Rotation"",
                    ""type"": ""Value"",
                    ""id"": ""4ff7f17c-26f9-4c23-a4b7-098a72c338bb"",
                    ""expectedControlType"": ""Quaternion"",
                    ""processors"": """",
                    ""interactions"": """"
                },
                {
                    ""name"": ""UI Press"",
                    ""type"": ""Button"",
                    ""id"": ""790e45a4-9f5e-417d-9f78-1383a4aa1a60"",
                    ""expectedControlType"": ""Button"",
                    ""processors"": """",
                    ""interactions"": """"
                },
                {
                    ""name"": ""Move"",
                    ""type"": ""Value"",
                    ""id"": ""b171783f-c6f8-4138-8837-07ec74b293b5"",
                    ""expectedControlType"": ""Vector2"",
                    ""processors"": """",
                    ""interactions"": """"
                },
                {
                    ""name"": ""Turn"",
                    ""type"": ""Value"",
                    ""id"": ""c24ad50f-d0c3-466b-a1f0-cc8e457fd4a1"",
                    ""expectedControlType"": ""Vector2"",
                    ""processors"": """",
                    ""interactions"": """"
                }
            ],
            ""bindings"": [
                {
                    ""name"": """",
                    ""id"": ""81f3570d-8460-462a-b120-5dc96ebdbff4"",
                    ""path"": ""<XRController>{LeftHand}/devicePosition"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": ""LogConsole"",
                    ""action"": ""Position"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""2b89a6ea-9434-44fd-ad03-1599b26743d6"",
                    ""path"": ""<XRController>{LeftHand}/deviceRotation"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": ""LogConsole"",
                    ""action"": ""Rotation"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""5b498c15-1a68-41b3-9e4e-d6c293c43022"",
                    ""path"": ""<XRController>{LeftHand}/triggerPressed"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": ""LogConsole"",
                    ""action"": ""UI Press"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""3227b379-ab1c-4179-a7c7-99148f93d0ba"",
                    ""path"": ""<XRController>{LeftHand}/primary2DAxis"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""Move"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""d0847802-b2b7-4f9b-8d75-7ba064bb4c74"",
                    ""path"": ""<XRController>{LeftHand}/primary2DAxis"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""Turn"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                }
            ]
        },
        {
            ""name"": ""XR Right Hand "",
            ""id"": ""f12fb4a4-f80d-4bce-a87c-c2085a5c9a8e"",
            ""actions"": [
                {
                    ""name"": ""Position"",
                    ""type"": ""Value"",
                    ""id"": ""67b99d2e-dd32-474b-8930-34bcb7de0f49"",
                    ""expectedControlType"": ""Vector3"",
                    ""processors"": """",
                    ""interactions"": """"
                },
                {
                    ""name"": ""Rotation"",
                    ""type"": ""Value"",
                    ""id"": ""60cbca1f-01de-45ce-85dd-d5f6259d6988"",
                    ""expectedControlType"": ""Quaternion"",
                    ""processors"": """",
                    ""interactions"": """"
                },
                {
                    ""name"": ""UI Press"",
                    ""type"": ""Button"",
                    ""id"": ""9020b3f6-8728-4ba1-97a2-203c7af5047b"",
                    ""expectedControlType"": ""Button"",
                    ""processors"": """",
                    ""interactions"": """"
                },
                {
                    ""name"": ""Move"",
                    ""type"": ""Value"",
                    ""id"": ""b290537b-9a1e-41bc-8cb6-288c23ba43b6"",
                    ""expectedControlType"": ""Vector2"",
                    ""processors"": """",
                    ""interactions"": """"
                },
                {
                    ""name"": ""Turn"",
                    ""type"": ""Value"",
                    ""id"": ""8771890f-a875-4384-9062-afc59fc02260"",
                    ""expectedControlType"": ""Vector2"",
                    ""processors"": """",
                    ""interactions"": """"
                }
            ],
            ""bindings"": [
                {
                    ""name"": """",
                    ""id"": ""e6eddd8e-ea2e-4acc-ae45-613083150a1d"",
                    ""path"": ""<XRController>{RightHand}/devicePosition"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": ""LogConsole"",
                    ""action"": ""Position"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""72babde0-3fc0-4c62-b5ad-f9ee701f97ac"",
                    ""path"": ""<XRController>{RightHand}/deviceRotation"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": ""LogConsole"",
                    ""action"": ""Rotation"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""d1d53322-9748-4d51-902c-af101678b707"",
                    ""path"": ""<XRController>{RightHand}/triggerPressed"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": ""LogConsole"",
                    ""action"": ""UI Press"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""c530d625-b8b7-4cbe-b90d-2f7851625b55"",
                    ""path"": ""<XRController>{RightHand}/primary2DAxis"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""Move"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""88598138-54e6-4b44-b88d-ed7655ed890d"",
                    ""path"": ""<XRController>{RightHand}/primary2DAxis"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""Turn"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                }
            ]
        }
    ],
    ""controlSchemes"": [
        {
            ""name"": ""LogConsole"",
            ""bindingGroup"": ""LogConsole"",
            ""devices"": []
        }
    ]
}");
        // XR Left Hand
        m_XRLeftHand = asset.FindActionMap("XR Left Hand", throwIfNotFound: true);
        m_XRLeftHand_Position = m_XRLeftHand.FindAction("Position", throwIfNotFound: true);
        m_XRLeftHand_Rotation = m_XRLeftHand.FindAction("Rotation", throwIfNotFound: true);
        m_XRLeftHand_UIPress = m_XRLeftHand.FindAction("UI Press", throwIfNotFound: true);
        m_XRLeftHand_Move = m_XRLeftHand.FindAction("Move", throwIfNotFound: true);
        m_XRLeftHand_Turn = m_XRLeftHand.FindAction("Turn", throwIfNotFound: true);
        // XR Right Hand 
        m_XRRightHand = asset.FindActionMap("XR Right Hand ", throwIfNotFound: true);
        m_XRRightHand_Position = m_XRRightHand.FindAction("Position", throwIfNotFound: true);
        m_XRRightHand_Rotation = m_XRRightHand.FindAction("Rotation", throwIfNotFound: true);
        m_XRRightHand_UIPress = m_XRRightHand.FindAction("UI Press", throwIfNotFound: true);
        m_XRRightHand_Move = m_XRRightHand.FindAction("Move", throwIfNotFound: true);
        m_XRRightHand_Turn = m_XRRightHand.FindAction("Turn", throwIfNotFound: true);
    }

    public void Dispose()
    {
        UnityEngine.Object.Destroy(asset);
    }

    public InputBinding? bindingMask
    {
        get => asset.bindingMask;
        set => asset.bindingMask = value;
    }

    public ReadOnlyArray<InputDevice>? devices
    {
        get => asset.devices;
        set => asset.devices = value;
    }

    public ReadOnlyArray<InputControlScheme> controlSchemes => asset.controlSchemes;

    public bool Contains(InputAction action)
    {
        return asset.Contains(action);
    }

    public IEnumerator<InputAction> GetEnumerator()
    {
        return asset.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public void Enable()
    {
        asset.Enable();
    }

    public void Disable()
    {
        asset.Disable();
    }

    // XR Left Hand
    private readonly InputActionMap m_XRLeftHand;
    private IXRLeftHandActions m_XRLeftHandActionsCallbackInterface;
    private readonly InputAction m_XRLeftHand_Position;
    private readonly InputAction m_XRLeftHand_Rotation;
    private readonly InputAction m_XRLeftHand_UIPress;
    private readonly InputAction m_XRLeftHand_Move;
    private readonly InputAction m_XRLeftHand_Turn;
    public struct XRLeftHandActions
    {
        private @ConsoleInput m_Wrapper;
        public XRLeftHandActions(@ConsoleInput wrapper) { m_Wrapper = wrapper; }
        public InputAction @Position => m_Wrapper.m_XRLeftHand_Position;
        public InputAction @Rotation => m_Wrapper.m_XRLeftHand_Rotation;
        public InputAction @UIPress => m_Wrapper.m_XRLeftHand_UIPress;
        public InputAction @Move => m_Wrapper.m_XRLeftHand_Move;
        public InputAction @Turn => m_Wrapper.m_XRLeftHand_Turn;
        public InputActionMap Get() { return m_Wrapper.m_XRLeftHand; }
        public void Enable() { Get().Enable(); }
        public void Disable() { Get().Disable(); }
        public bool enabled => Get().enabled;
        public static implicit operator InputActionMap(XRLeftHandActions set) { return set.Get(); }
        public void SetCallbacks(IXRLeftHandActions instance)
        {
            if (m_Wrapper.m_XRLeftHandActionsCallbackInterface != null)
            {
                @Position.started -= m_Wrapper.m_XRLeftHandActionsCallbackInterface.OnPosition;
                @Position.performed -= m_Wrapper.m_XRLeftHandActionsCallbackInterface.OnPosition;
                @Position.canceled -= m_Wrapper.m_XRLeftHandActionsCallbackInterface.OnPosition;
                @Rotation.started -= m_Wrapper.m_XRLeftHandActionsCallbackInterface.OnRotation;
                @Rotation.performed -= m_Wrapper.m_XRLeftHandActionsCallbackInterface.OnRotation;
                @Rotation.canceled -= m_Wrapper.m_XRLeftHandActionsCallbackInterface.OnRotation;
                @UIPress.started -= m_Wrapper.m_XRLeftHandActionsCallbackInterface.OnUIPress;
                @UIPress.performed -= m_Wrapper.m_XRLeftHandActionsCallbackInterface.OnUIPress;
                @UIPress.canceled -= m_Wrapper.m_XRLeftHandActionsCallbackInterface.OnUIPress;
                @Move.started -= m_Wrapper.m_XRLeftHandActionsCallbackInterface.OnMove;
                @Move.performed -= m_Wrapper.m_XRLeftHandActionsCallbackInterface.OnMove;
                @Move.canceled -= m_Wrapper.m_XRLeftHandActionsCallbackInterface.OnMove;
                @Turn.started -= m_Wrapper.m_XRLeftHandActionsCallbackInterface.OnTurn;
                @Turn.performed -= m_Wrapper.m_XRLeftHandActionsCallbackInterface.OnTurn;
                @Turn.canceled -= m_Wrapper.m_XRLeftHandActionsCallbackInterface.OnTurn;
            }
            m_Wrapper.m_XRLeftHandActionsCallbackInterface = instance;
            if (instance != null)
            {
                @Position.started += instance.OnPosition;
                @Position.performed += instance.OnPosition;
                @Position.canceled += instance.OnPosition;
                @Rotation.started += instance.OnRotation;
                @Rotation.performed += instance.OnRotation;
                @Rotation.canceled += instance.OnRotation;
                @UIPress.started += instance.OnUIPress;
                @UIPress.performed += instance.OnUIPress;
                @UIPress.canceled += instance.OnUIPress;
                @Move.started += instance.OnMove;
                @Move.performed += instance.OnMove;
                @Move.canceled += instance.OnMove;
                @Turn.started += instance.OnTurn;
                @Turn.performed += instance.OnTurn;
                @Turn.canceled += instance.OnTurn;
            }
        }
    }
    public XRLeftHandActions @XRLeftHand => new XRLeftHandActions(this);

    // XR Right Hand 
    private readonly InputActionMap m_XRRightHand;
    private IXRRightHandActions m_XRRightHandActionsCallbackInterface;
    private readonly InputAction m_XRRightHand_Position;
    private readonly InputAction m_XRRightHand_Rotation;
    private readonly InputAction m_XRRightHand_UIPress;
    private readonly InputAction m_XRRightHand_Move;
    private readonly InputAction m_XRRightHand_Turn;
    public struct XRRightHandActions
    {
        private @ConsoleInput m_Wrapper;
        public XRRightHandActions(@ConsoleInput wrapper) { m_Wrapper = wrapper; }
        public InputAction @Position => m_Wrapper.m_XRRightHand_Position;
        public InputAction @Rotation => m_Wrapper.m_XRRightHand_Rotation;
        public InputAction @UIPress => m_Wrapper.m_XRRightHand_UIPress;
        public InputAction @Move => m_Wrapper.m_XRRightHand_Move;
        public InputAction @Turn => m_Wrapper.m_XRRightHand_Turn;
        public InputActionMap Get() { return m_Wrapper.m_XRRightHand; }
        public void Enable() { Get().Enable(); }
        public void Disable() { Get().Disable(); }
        public bool enabled => Get().enabled;
        public static implicit operator InputActionMap(XRRightHandActions set) { return set.Get(); }
        public void SetCallbacks(IXRRightHandActions instance)
        {
            if (m_Wrapper.m_XRRightHandActionsCallbackInterface != null)
            {
                @Position.started -= m_Wrapper.m_XRRightHandActionsCallbackInterface.OnPosition;
                @Position.performed -= m_Wrapper.m_XRRightHandActionsCallbackInterface.OnPosition;
                @Position.canceled -= m_Wrapper.m_XRRightHandActionsCallbackInterface.OnPosition;
                @Rotation.started -= m_Wrapper.m_XRRightHandActionsCallbackInterface.OnRotation;
                @Rotation.performed -= m_Wrapper.m_XRRightHandActionsCallbackInterface.OnRotation;
                @Rotation.canceled -= m_Wrapper.m_XRRightHandActionsCallbackInterface.OnRotation;
                @UIPress.started -= m_Wrapper.m_XRRightHandActionsCallbackInterface.OnUIPress;
                @UIPress.performed -= m_Wrapper.m_XRRightHandActionsCallbackInterface.OnUIPress;
                @UIPress.canceled -= m_Wrapper.m_XRRightHandActionsCallbackInterface.OnUIPress;
                @Move.started -= m_Wrapper.m_XRRightHandActionsCallbackInterface.OnMove;
                @Move.performed -= m_Wrapper.m_XRRightHandActionsCallbackInterface.OnMove;
                @Move.canceled -= m_Wrapper.m_XRRightHandActionsCallbackInterface.OnMove;
                @Turn.started -= m_Wrapper.m_XRRightHandActionsCallbackInterface.OnTurn;
                @Turn.performed -= m_Wrapper.m_XRRightHandActionsCallbackInterface.OnTurn;
                @Turn.canceled -= m_Wrapper.m_XRRightHandActionsCallbackInterface.OnTurn;
            }
            m_Wrapper.m_XRRightHandActionsCallbackInterface = instance;
            if (instance != null)
            {
                @Position.started += instance.OnPosition;
                @Position.performed += instance.OnPosition;
                @Position.canceled += instance.OnPosition;
                @Rotation.started += instance.OnRotation;
                @Rotation.performed += instance.OnRotation;
                @Rotation.canceled += instance.OnRotation;
                @UIPress.started += instance.OnUIPress;
                @UIPress.performed += instance.OnUIPress;
                @UIPress.canceled += instance.OnUIPress;
                @Move.started += instance.OnMove;
                @Move.performed += instance.OnMove;
                @Move.canceled += instance.OnMove;
                @Turn.started += instance.OnTurn;
                @Turn.performed += instance.OnTurn;
                @Turn.canceled += instance.OnTurn;
            }
        }
    }
    public XRRightHandActions @XRRightHand => new XRRightHandActions(this);
    private int m_LogConsoleSchemeIndex = -1;
    public InputControlScheme LogConsoleScheme
    {
        get
        {
            if (m_LogConsoleSchemeIndex == -1) m_LogConsoleSchemeIndex = asset.FindControlSchemeIndex("LogConsole");
            return asset.controlSchemes[m_LogConsoleSchemeIndex];
        }
    }
    public interface IXRLeftHandActions
    {
        void OnPosition(InputAction.CallbackContext context);
        void OnRotation(InputAction.CallbackContext context);
        void OnUIPress(InputAction.CallbackContext context);
        void OnMove(InputAction.CallbackContext context);
        void OnTurn(InputAction.CallbackContext context);
    }
    public interface IXRRightHandActions
    {
        void OnPosition(InputAction.CallbackContext context);
        void OnRotation(InputAction.CallbackContext context);
        void OnUIPress(InputAction.CallbackContext context);
        void OnMove(InputAction.CallbackContext context);
        void OnTurn(InputAction.CallbackContext context);
    }
}
