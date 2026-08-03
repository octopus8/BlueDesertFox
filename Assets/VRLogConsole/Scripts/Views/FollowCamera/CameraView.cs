using System;
using UnityEngine;
using VRLogConsole.Scripts.Presenters.FollowCamera;
using VRLogConsole.Scripts.Properties.FollowCamera;
using VRLogConsole.Scripts.Types.Lock;
using VRLogConsole.Scripts.Views.Base;

namespace VRLogConsole.Scripts.Views.FollowCamera
{
    public class CameraView : ViewBehaviour<CameraPresenter,CameraProperties>
    {
        private bool _configLoaded;
        private Vector3 _defaultPosition;

        protected override void Initialize()
        {
            AssignCamera();
        }

        protected override void PostInitialize()
        {
            _defaultPosition = new Vector3(0.5f,0.5f,Presenter.DistanceFromCamera);
            _configLoaded = true;
        }

        private void Update()
        {
            if (_configLoaded)
            {
                UpdatePosition();
                UpdateRotation();
                UpdateLabel();
            }
        }

        private void AssignCamera()
        {
            if (Prefab.cameraToFollow == null && Camera.main != null)
            {
                var main = Camera.main;
                Prefab.cameraToFollow = main;
            }
            
            if (Prefab.cameraToFollow == null)
            {
                throw new Exception("Camera is not assigned, please assign a camera at VR Log Console");
            }
            
            Prefab.canvas.worldCamera = Prefab.cameraToFollow;
        }
        
        private void UpdatePosition()
        {
            if (Presenter.LockType == LockType.Unlocked)
            {
                Prefab.consoleTransform.position = Prefab.cameraToFollow.ViewportToWorldPoint(_defaultPosition);
            }
        }

        private void UpdateRotation()
        {
            if (Presenter.LookAtPlayer)
            {
                var targetPosition = Prefab.cameraToFollow.transform.position;
                Prefab.consoleTransform.rotation = Quaternion.LookRotation(Prefab.consoleTransform.position - targetPosition);
            }
        }
        
        private void UpdateLabel()
        {
            var position = Prefab.consoleTransform.position;
            var rotation = Prefab.consoleTransform.rotation.eulerAngles;
            
            Prefab.positionText.text = $"Position {position} - Rotation {rotation}";
        }
    }
}
