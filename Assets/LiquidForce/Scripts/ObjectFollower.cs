using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Rendering;

namespace LiquidForce
{
    
//    [DefaultExecutionOrder(32000)]
    public class ObjectFollower : MonoBehaviour
    {
        public enum Moment
        {
            OnFixedUpdate,
            OnUpdate,
            OnLateUpdate,
            OnPreRender,
            OnPreCull
        }

        private bool oneTimeUpdate;

        
        [field: SerializeField] public Transform source { private get; set; }
        
        [SerializeField] private List<Transform> targets;
        
        [field: SerializeField] public Moment moment { private get; set; } = Moment.OnUpdate;
        

        [SerializeField] private Vector3 maxRotationOffsetDegrees;

        [SerializeField] private Vector3 maxPositionOffset;

        [SerializeField]
        float positionSpeed = 1f;
        
        [SerializeField]
        float rotationSpeed = 0.1f;


        [Header("Update")]
        [SerializeField] private bool updateRotationX = true;
        [SerializeField] private bool updateRotationY = true;
        [SerializeField] private bool updateRotationZ = true;


        
        
        
        
        
        

        private void Awake()
        {
            // Subscribe to begin camera rendering event to handle "on pre render" moments.
            RenderPipelineManager.beginCameraRendering += RenderPipelineManager_beginCameraRendering;
        }

        private void OnDestroy()
        {
            // Unsubscribe to events.
            RenderPipelineManager.beginCameraRendering -= RenderPipelineManager_beginCameraRendering;
        }

        public void OnEnable()
        {
            oneTimeUpdate = true;
        }

        public void AddTarget(Transform target)
        {
            if (targets == null)
            {
                targets = new List<Transform>();
            }
            targets.Add(target);
        }

        public void ClearTargets()
        {
            targets.Clear();
        }

        private void FixedUpdate()
        {
            if (moment == Moment.OnFixedUpdate)
            {
                UpdateTransform();    
            }
            
        }

        private void Update()
        {
            if (moment == Moment.OnUpdate)
            {
                UpdateTransform();    
            }
        }

        private void LateUpdate()
        {
            if (moment == Moment.OnLateUpdate)
            {
                UpdateTransform();    
            }
        }

        private void OnPreRender()
        {
            if (moment == Moment.OnPreRender)
            {
                UpdateTransform();    
            }
        }

        /// <summary>
        /// Callback called upon pre-render, updates the transform if the moment is "OnPreRender".
        /// </summary>
        /// <remarks>
        /// If you are using the Universal Render Pipeline (URP) or High Definition Render Pipeline (HDRP), the MonoBehaviour.OnPreRender() callback is deprecated and will not work.
        /// </remarks>
        /// <param name="context"></param>
        /// <param name="camera"></param>
        private void RenderPipelineManager_beginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (moment == Moment.OnPreRender)
            {
                UpdateTransform();    
            }
        }

        private void OnPreCull()
        {
            if (moment == Moment.OnPreCull)
            {
                UpdateTransform();    
            }
            
        }

        private void UpdateTransform()
        {
            // If no targets have been set, then do nothing.
            if (targets == null)
            {
                return;
            }

            if (null == source)
            {
                source = gameObject.transform;
            }

            foreach (var target in targets)
            {
                if (target == null)
                {
                    Debug.Log("ObjectFollower: target is null.");
                    return;
                }
                Vector3 targetPosition = target.position;
                Vector3 targetRotation = target.rotation.eulerAngles;

                if (Mathf.Abs(targetPosition.x - source.position.x) > maxPositionOffset.x)
                {
                    targetPosition.x = Mathf.Lerp(targetPosition.x, source.position.x, positionSpeed * Time.deltaTime);
                }

                if (Mathf.Abs(targetPosition.y - source.position.y) > maxPositionOffset.y)
                {
                    targetPosition.y = Mathf.Lerp(targetPosition.y, source.position.y, positionSpeed * Time.deltaTime);
                }

                if (Mathf.Abs(targetPosition.z - source.position.z) > maxPositionOffset.z)
                {
                    targetPosition.z = Mathf.Lerp(targetPosition.z, source.position.z, positionSpeed * Time.deltaTime);
                }


                if (updateRotationX)
                {
                    if (Mathf.Abs(targetRotation.x - source.rotation.eulerAngles.x) > maxRotationOffsetDegrees.x)
                    {
                        targetRotation.x = Mathf.Lerp(targetRotation.x, source.rotation.eulerAngles.x, rotationSpeed * Time.deltaTime);
                    }
                }
                if (updateRotationY)
                {
                    if (Mathf.Abs(targetRotation.y - source.rotation.eulerAngles.y) > maxRotationOffsetDegrees.y)
                    {
                        targetRotation.y = Mathf.Lerp(targetRotation.y, source.rotation.eulerAngles.y, rotationSpeed * Time.deltaTime);
                    }
                }
                if (updateRotationZ)
                {
                    if (Mathf.Abs(targetRotation.z - source.rotation.eulerAngles.z) > maxRotationOffsetDegrees.z)
                    {
                        targetRotation.z = Mathf.Lerp(targetRotation.z, source.rotation.eulerAngles.z, rotationSpeed * Time.deltaTime);
                    }
                }
                
                
                target.SetPositionAndRotation(targetPosition, Quaternion.Euler(targetRotation));
            }
            oneTimeUpdate = false;
        }


        private bool isSettingPositionX;
        private bool isSettingPositionY;
        private bool isSettingPositionZ;
        private bool isSettingRotationX;
        private bool isSettingRotationY;
        private bool isSettingRotationZ;
        
        private void UpdateTransformNew()
        {
            // If no targets have been set, then do nothing.
            if (targets == null)
            {
                return;
            }

            if (null == source)
            {
                source = gameObject.transform;
            }

            foreach (var target in targets)
            {
                if (target == null)
                {
                    Debug.Log("ObjectFollower: target is null.");
                    return;
                }
                Vector3 targetPosition = target.position;
                Vector3 targetRotation = target.rotation.eulerAngles;
                
                float positionSetMargin = 0.01f;
                float rotationSetMargin = 1.0f;
                float abs = Mathf.Abs(targetPosition.x - source.position.x);
                if (abs < positionSetMargin)
                {
                    isSettingPositionX = false;
                }
                if (abs > maxPositionOffset.x || isSettingPositionX)
                {
                    targetPosition.x = Mathf.Lerp(targetPosition.x, source.position.x, positionSpeed * Time.deltaTime);
                }
                
                abs = Mathf.Abs(targetPosition.y - source.position.y);
                if (abs < positionSetMargin)
                {
                    isSettingPositionY = false;
                }

                if (abs > maxPositionOffset.y || isSettingPositionY)
                {
                    targetPosition.y = Mathf.Lerp(targetPosition.y, source.position.y, positionSpeed * Time.deltaTime);
                }
                
                abs = Mathf.Abs(targetPosition.z - source.position.z);
                if (abs < positionSetMargin)
                {
                    isSettingPositionZ = false;
                }

                if (abs > maxPositionOffset.z || isSettingPositionZ)
                {
                    targetPosition.z = Mathf.Lerp(targetPosition.z, source.position.z, rotationSpeed * Time.deltaTime);
                }
                
                abs = Mathf.Abs(targetRotation.x - source.rotation.eulerAngles.x);
                if (abs < rotationSetMargin)
                {
                    isSettingRotationX = false;
                }

                if (abs > maxRotationOffsetDegrees.x || isSettingRotationX)
                {
                    targetRotation.x = Mathf.Lerp(targetRotation.x, source.rotation.x, rotationSpeed * Time.deltaTime);
                }
                
                abs = Mathf.Abs(targetRotation.y - source.rotation.y);
                if (abs < rotationSetMargin)
                {
                    isSettingRotationY = false;
                }

                if (abs > maxRotationOffsetDegrees.y || isSettingRotationY)
                {
                    targetRotation.y = Mathf.Lerp(targetRotation.y, source.rotation.y, rotationSpeed * Time.deltaTime);
                }
                
                abs = Mathf.Abs(targetRotation.z - source.rotation.z);
                if (abs < rotationSetMargin)
                {
                    isSettingRotationZ = false;
                }

                if (abs > maxRotationOffsetDegrees.z || isSettingRotationZ)
                {
                    targetRotation.z = Mathf.Lerp(targetRotation.z, source.rotation.z, rotationSpeed * Time.deltaTime);
                }
                
                target.SetPositionAndRotation(targetPosition, Quaternion.Euler(targetRotation));
            }
            oneTimeUpdate = false;
        }
        
        
    }        
}
    
