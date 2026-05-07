using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace BattleChess
{
    public enum PointerPhase
    {
        Began,
        Moved,
        Ended,
        Canceled,
        Stationary
    }

    public readonly struct PointerContact
    {
        public readonly int Id;
        public readonly Vector2 GuiPosition;
        public readonly PointerPhase Phase;

        public PointerContact(int id, Vector2 guiPosition, PointerPhase phase)
        {
            Id = id;
            GuiPosition = guiPosition;
            Phase = phase;
        }

        public bool IsFinished => Phase == PointerPhase.Ended || Phase == PointerPhase.Canceled;
    }

    /// <summary>
    /// Reads Board finger contacts when the SDK is installed, then falls back to Unity touch/mouse input
    /// for editor iteration. Board SDK types are accessed by reflection so this can compile before the
    /// package tarball is imported.
    /// </summary>
    public sealed class BoardPointerInput
    {
        private readonly List<PointerContact> contacts = new();
        private readonly Dictionary<int, Vector2> previousPositions = new();

        private bool searchedForBoardSdk;
        private bool boardSdkAvailable;
        private Type boardInputType;
        private Type boardContactType;
        private Type boardContactPhaseType;
        private object fingerContactTypeValue;
        private MethodInfo getActiveContactsMethod;

        private PropertyInfo contactIdProperty;
        private PropertyInfo screenPositionProperty;
        private PropertyInfo phaseProperty;

        public IReadOnlyList<PointerContact> Poll()
        {
            contacts.Clear();

            if (!searchedForBoardSdk)
            {
                TryInitializeBoardSdk();
            }

            if (boardSdkAvailable && TryPollBoardContacts() && contacts.Count > 0)
            {
                return contacts;
            }

            contacts.Clear();
            PollInputSystemInput();
            return contacts;
        }

        private void TryInitializeBoardSdk()
        {
            searchedForBoardSdk = true;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                boardInputType ??= assembly.GetType("Board.Input.BoardInput");
                boardContactType ??= assembly.GetType("Board.Input.BoardContactType");
                boardContactPhaseType ??= assembly.GetType("Board.Input.BoardContactPhase");

                if (boardInputType != null && boardContactType != null && boardContactPhaseType != null)
                {
                    break;
                }
            }

            if (boardInputType == null || boardContactType == null || boardContactPhaseType == null)
            {
                return;
            }

            getActiveContactsMethod = boardInputType
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(method =>
                {
                    if (method.Name != "GetActiveContacts")
                    {
                        return false;
                    }

                    ParameterInfo[] parameters = method.GetParameters();
                    return parameters.Length == 1
                           && parameters[0].ParameterType.IsArray
                           && parameters[0].ParameterType.GetElementType() == boardContactType;
                });

            if (getActiveContactsMethod == null)
            {
                return;
            }

            fingerContactTypeValue = Enum.Parse(boardContactType, "Finger");
            boardSdkAvailable = true;
        }

        private bool TryPollBoardContacts()
        {
            try
            {
                Array requestedTypes = Array.CreateInstance(boardContactType, 1);
                requestedTypes.SetValue(fingerContactTypeValue, 0);

                Array boardContacts = (Array)getActiveContactsMethod.Invoke(null, new object[] { requestedTypes });
                if (boardContacts == null)
                {
                    return false;
                }

                foreach (object contact in boardContacts)
                {
                    CacheContactProperties(contact.GetType());

                    int id = (int)contactIdProperty.GetValue(contact);
                    Vector2 boardScreenPosition = (Vector2)screenPositionProperty.GetValue(contact);
                    object boardPhase = phaseProperty.GetValue(contact);
                    PointerPhase phase = ConvertBoardPhase(boardPhase);
                    contacts.Add(new PointerContact(id, ToGuiPosition(boardScreenPosition), phase));
                }

                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Board input polling failed, falling back to Unity input. {exception.Message}");
                boardSdkAvailable = false;
                return false;
            }
        }

        private void CacheContactProperties(Type contactType)
        {
            if (contactIdProperty != null)
            {
                return;
            }

            contactIdProperty = contactType.GetProperty("contactId");
            screenPositionProperty = contactType.GetProperty("screenPosition");
            phaseProperty = contactType.GetProperty("phase");
        }

        private PointerPhase ConvertBoardPhase(object boardPhase)
        {
            string phaseName = Enum.GetName(boardContactPhaseType, boardPhase);
            return phaseName switch
            {
                "Began" => PointerPhase.Began,
                "Moved" => PointerPhase.Moved,
                "Ended" => PointerPhase.Ended,
                "Canceled" => PointerPhase.Canceled,
                _ => PointerPhase.Stationary
            };
        }

        private void PollInputSystemInput()
        {
            Touchscreen touchscreen = Touchscreen.current;
            bool sawTouch = false;

            if (touchscreen != null)
            {
                foreach (TouchControl touch in touchscreen.touches)
                {
                    UnityEngine.InputSystem.TouchPhase phase = touch.phase.ReadValue();
                    if (phase == UnityEngine.InputSystem.TouchPhase.None)
                    {
                        continue;
                    }

                    bool isActive = touch.press.isPressed
                                    || phase == UnityEngine.InputSystem.TouchPhase.Ended
                                    || phase == UnityEngine.InputSystem.TouchPhase.Canceled;
                    if (!isActive)
                    {
                        continue;
                    }

                    contacts.Add(new PointerContact(touch.touchId.ReadValue(), ToGuiPosition(touch.position.ReadValue()), ConvertTouchPhase(phase)));
                    sawTouch = true;
                }
            }

            if (sawTouch)
            {
                return;
            }

            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            const int mouseId = -1;
            Vector2 mousePosition = ToGuiPosition(mouse.position.ReadValue());
            ButtonControl leftButton = mouse.leftButton;

            if (leftButton.wasPressedThisFrame)
            {
                previousPositions[mouseId] = mousePosition;
                contacts.Add(new PointerContact(mouseId, mousePosition, PointerPhase.Began));
                return;
            }

            if (leftButton.isPressed)
            {
                bool moved = !previousPositions.TryGetValue(mouseId, out Vector2 previous)
                             || Vector2.Distance(previous, mousePosition) > 0.5f;
                previousPositions[mouseId] = mousePosition;
                contacts.Add(new PointerContact(mouseId, mousePosition, moved ? PointerPhase.Moved : PointerPhase.Stationary));
                return;
            }

            if (leftButton.wasReleasedThisFrame)
            {
                previousPositions.Remove(mouseId);
                contacts.Add(new PointerContact(mouseId, mousePosition, PointerPhase.Ended));
            }
        }

        private static PointerPhase ConvertTouchPhase(UnityEngine.InputSystem.TouchPhase phase)
        {
            return phase switch
            {
                UnityEngine.InputSystem.TouchPhase.Began => PointerPhase.Began,
                UnityEngine.InputSystem.TouchPhase.Moved => PointerPhase.Moved,
                UnityEngine.InputSystem.TouchPhase.Ended => PointerPhase.Ended,
                UnityEngine.InputSystem.TouchPhase.Canceled => PointerPhase.Canceled,
                _ => PointerPhase.Stationary
            };
        }

        private static Vector2 ToGuiPosition(Vector2 screenPosition)
        {
            return new Vector2(screenPosition.x, Screen.height - screenPosition.y);
        }
    }
}
