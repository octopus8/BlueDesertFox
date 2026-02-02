using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LiquidForce
{

    /// <summary>
    /// This component is used to make one object follow another.
    /// </summary>
    /// <remarks>
    /// - If following a RigidBody, this calculation seems to be a frame off.
    /// </remarks>
    public class ObjectFollower : MonoBehaviour
    {
        /// <summary>The moment to update the target transform.</summary>
        public enum Moment
        {
            OnFixedUpdate,
            OnUpdate,
            OnLateUpdate,
            OnPreRender,
            OnPreCull
        }
        
        /// <summary>The transform the target transforms are set to.</summary>
        [field: SerializeField] public Transform source { private get; set; }

        
        /// <summary>The transforms that are set to the source transform.</summary>
        [SerializeField] private List<Transform> targets;

        /// <summary>The moment to update the transform.</summary>
        [field: SerializeField] public Moment moment { private get; set; } = Moment.OnUpdate;
        
        /// <summary>Allowable rotation offset before lerping.</summary>
        [SerializeField] private Vector3 maxRotationOffsetDegrees;

        /// <summary>Allowable position offset before lerping.</summary>
        [SerializeField] private Vector3 maxPositionOffset;

        /// <summary>The position lerp speed.</summary>
        [SerializeField] private float positionSpeed = 20f;
        
        /// <summary>The rotation lerp speed.</summary>
        [SerializeField] private float rotationSpeed = 10f;


        [Header("Update")]
        /// <summary>Flag indicating update X rotation.</summary>
        [SerializeField] private bool updateRotationX = true;
        /// <summary>Flag indicating update X rotation.</summary>
        [SerializeField] private bool updateRotationY = true;
        /// <summary>Flag indicating update X rotation.</summary>
        [SerializeField] private bool updateRotationZ = true;

        
        /// Flags indicating a position/rotation is being set.
        /// This flag is set upon the value being beyond allowable offsets, and cleared upon lerping within a tollerance.
        /// This allows the tollerance to be lower than the allowable offset.
        private bool isSettingPositionX;
        private bool isSettingPositionY;
        private bool isSettingPositionZ;
        private bool isSettingRotationX;
        private bool isSettingRotationY;
        private bool isSettingRotationZ;
        
        
        /// <summary>Flag indicating to snap the target to the source transform instead of lerping.</summary>
        private bool snapTo;
        
        

        /// <summary>
        /// Initializes the component.
        /// </summary>
        private void Awake()
        {
            // Subscribe to begin camera rendering event to handle "on pre render" moments.
            RenderPipelineManager.beginCameraRendering += RenderPipelineManager_beginCameraRendering;
        }

        /// <summary>
        /// Uninitializes the component.
        /// </summary>
        private void OnDestroy()
        {
            // Unsubscribe to events.
            RenderPipelineManager.beginCameraRendering -= RenderPipelineManager_beginCameraRendering;
        }

        
        /// <summary>
        /// Performs enable initialization.
        /// </summary>
        public void OnEnable()
        {
            snapTo = true;
        }

        
        /// <summary>
        /// Adds a following target.
        /// </summary>
        /// <param name="target"></param>
        public void AddTarget(Transform target)
        {
            if (targets == null)
            {
                targets = new List<Transform>();
            }
            targets.Add(target);
        }

        
        /// <summary>
        /// Clears the set of targets.
        /// </summary>
        public void ClearTargets()
        {
            targets.Clear();
        }

        
        /// <summary>
        /// Updates the targets if appropriate.
        /// </summary>
        private void FixedUpdate()
        {
            if (moment == Moment.OnFixedUpdate)
            {
                UpdateTargetTransforms();    
            }
            
        }

        
        /// <summary>
        /// Updates the targets if appropriate.
        /// </summary>
        private void Update()
        {
            if (moment == Moment.OnUpdate)
            {
                UpdateTargetTransforms();    
            }
        }

        
        /// <summary>
        /// Updates the targets if appropriate.
        /// </summary>
        private void LateUpdate()
        {
            if (moment == Moment.OnLateUpdate)
            {
                UpdateTargetTransforms();    
            }
        }

        
        /// <summary>
        /// Updates the targets if appropriate.
        /// </summary>
        private void OnPreRender()
        {
            if (moment == Moment.OnPreRender)
            {
                UpdateTargetTransforms();    
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
                UpdateTargetTransforms();    
            }
        }

        
        /// <summary>
        /// Updates the targets if appropriate.
        /// </summary>
        private void OnPreCull()
        {
            if (moment == Moment.OnPreCull)
            {
                UpdateTargetTransforms();    
            }
            
        }

        
        private void UpdateTargetTransforms()
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
    }        
}
    
