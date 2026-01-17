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

        private bool snapTo;

        
        [field: SerializeField] public Transform source { private get; set; }
        
        [SerializeField] private List<Transform> targets;
        
        [field: SerializeField] public Moment moment { private get; set; } = Moment.OnUpdate;
        

        [SerializeField] private Vector3 maxRotationOffsetDegrees;

        [SerializeField] private Vector3 maxPositionOffset;

        [SerializeField]
        float positionSpeed = 20f;
        
        [SerializeField]
        float rotationSpeed = 10f;


        [Header("Update")]
        [SerializeField] private bool updateRotationX = true;
        [SerializeField] private bool updateRotationY = true;
        [SerializeField] private bool updateRotationZ = true;


        
        
        private bool isSettingPositionX;
        private bool isSettingPositionY;
        private bool isSettingPositionZ;
        private bool isSettingRotationX;
        private bool isSettingRotationY;
        private bool isSettingRotationZ;
        
        
        
        

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
            snapTo = true;
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
            float positionReachedTolerance = 0.1f;
            float rotationReachedTollerance = 1.0f;
            
            // If no targets have been set, then do nothing.
            if (targets == null)
            {
                return;
            }

            // If no source has been set, then set it to the component's GameObject.
            if (null == source)
            {
                source = gameObject.transform;
            }

            // If "snap to", then snap to.
            if (snapTo)
            {
                snapTo = false;
                foreach (var target in targets)
                {
                    if (target == null)
                    {
                        Debug.Log("ObjectFollower: target is null.");
                        return;
                    }
                    target.SetPositionAndRotation(source.position, source.rotation);
                }

                return;
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
                if (targetRotation.x > 180)
                {
                    targetRotation.x -= 360;
                }

                if (targetRotation.y > 180)
                {
                    targetRotation.y -= 360;
                }

                if (targetRotation.z > 180)
                {
                    targetRotation.z -= 360;
                }

                // If the offset is within tolerance, then consider position set.
                float dif =  Mathf.Abs(targetPosition.x - source.position.x);
                if (dif < positionReachedTolerance)
                {
                    isSettingPositionX = false;
                }
                // If beyond the max offset or setting the position, then start/continue setting the position.
                if (dif > maxPositionOffset.x || isSettingPositionX)
                {
                    isSettingPositionX = true;
                    targetPosition.x = Mathf.Lerp(targetPosition.x, source.position.x, positionSpeed * Time.deltaTime);
                }

                // If the offset is within tolerance, then consider position set.
                dif =  Mathf.Abs(targetPosition.y - source.position.y);
                if (dif < positionReachedTolerance)
                {
                    isSettingPositionY = false;
                }
                // If beyond the max offset or setting the position, then start/continue setting the position.
                if (dif > maxPositionOffset.y || isSettingPositionY)
                {
                    isSettingPositionY = true;
                    targetPosition.y = Mathf.Lerp(targetPosition.y, source.position.y, positionSpeed * Time.deltaTime);
                }

                // If the offset is within tolerance, then consider position set.
                dif =  Mathf.Abs(targetPosition.z - source.position.z);
                if (dif < positionReachedTolerance)
                {
                    isSettingPositionZ = false;
                }
                // If beyond the max offset or setting the position, then start/continue setting the position.
                if (dif > maxPositionOffset.z || isSettingPositionZ)
                {
                    isSettingPositionZ = true;
                    targetPosition.z = Mathf.Lerp(targetPosition.z, source.position.z, positionSpeed * Time.deltaTime);
                }


                if (updateRotationX)
                {
                    dif = Mathf.Abs(targetRotation.x - source.rotation.eulerAngles.x);
                    if (dif < rotationReachedTollerance)
                    {
                        isSettingRotationX = false;
                    }
                    if (dif > maxRotationOffsetDegrees.x || isSettingRotationX)
                    {
                        isSettingRotationX = true;
                        targetRotation.x = Mathf.Lerp(targetRotation.x, source.rotation.eulerAngles.x, rotationSpeed * Time.deltaTime);
                    }
                }
                if (updateRotationY)
                {
                    var targetRot = targetRotation.y;
                    dif = Mathf.Abs(targetRot - source.rotation.eulerAngles.y);
                    if (dif > 180.0f)
                    {
                        targetRot += 360.0f;
                        dif = Mathf.Abs(targetRot - source.rotation.eulerAngles.y);
                    }
                    if (dif < rotationReachedTollerance)
                    {
                        isSettingRotationY = false;
                    }
                    if (dif > maxRotationOffsetDegrees.y || isSettingRotationY)
                    {
                        isSettingRotationY = true;
                        targetRotation.y = Mathf.Lerp(targetRot, source.rotation.eulerAngles.y, rotationSpeed * Time.deltaTime);
                    }
                }
                if (updateRotationZ)
                {
                    dif = Mathf.Abs(targetRotation.z - source.rotation.eulerAngles.z);
                    if (dif < rotationReachedTollerance)
                    {
                        isSettingRotationZ = false;
                    }
                    if (dif > maxRotationOffsetDegrees.z || isSettingRotationZ)
                    {
                        isSettingRotationZ = true;
                        targetRotation.z = Mathf.Lerp(targetRotation.z, source.rotation.eulerAngles.z, rotationSpeed * Time.deltaTime);
                    }
                }
                
                
                target.SetPositionAndRotation(targetPosition, Quaternion.Euler(targetRotation));
            }
        }


        
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
        }
        
        
    }        
}
    
