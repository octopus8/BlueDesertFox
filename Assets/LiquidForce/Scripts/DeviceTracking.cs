using System;
using UnityEngine;

namespace LiquidForce
{
    /// <summary>
    /// A required component of the Application, this component provides functionality for object to follow tracked devices.
    /// </summary>
    public class DeviceTracking : MonoBehaviour
    {
        [SerializeField] private Transform trackingOrigin;
        
        /// <summary>
        /// The source head GameObject.
        /// </summary>
        /// <remarks>
        /// - This is used as the source for the head object follower, which is used by `CameraFader` to follow the head.
        /// </remarks>
        [SerializeField]
        private Transform head;
        
        /// <summary>
        /// The head object follower, added to the GameObject by `Awake`.
        /// </summary>
        private ObjectFollower headObjectFollower;
        
        static public DeviceTracking Instance { get; private set; }
        
        /// <summary>
        /// The parent transform for tracked devices.
        /// </summary>
        /// <remarks>
        /// This is used by `SceneStartup` to set the initial starting position.
        /// </remarks>
        public Transform TrackingOrigin => trackingOrigin;

        /// <summary>
        /// The tracked head / camera transform used as the ObjectFollower source.
        /// </summary>
        public Transform Head => head;


        private void Awake()
        {
            // If this isn't the first instance of this component, destroy this component.
            if (null != Instance)
            {
                Destroy(this);
                return;
            }
            
            // Store the instance of this component.
            Instance = this;
            
            // Add and initialize the head object follower component.
            headObjectFollower = gameObject.AddComponent<ObjectFollower>();
            headObjectFollower.moment = ObjectFollower.Moment.OnPreRender;

            // Set the head object follower source.
            if (null == head)
            {
                Debug.LogError($"Head object not specified.");
                return;
            }
            headObjectFollower.source = head;
        }

        private void OnDestroy()
        {
            // If this is the instance of this component, clear the instance.
            if (Instance == this)
            {
                Instance = null;
            }
        }


        /// <summary>
        /// Adds an object to follow the head.
        /// </summary>
        /// <param name="target">The object to follow the head.</param>
        public void AddHeadFollower(Transform target)
        {
            headObjectFollower.AddTarget(target);
            UpdateImmediate();
        }

        public void RemoveHeadFollower(Transform target)
        {
            headObjectFollower.RemoveTarget(target);
        }

        public void UpdateImmediate()
        {
            headObjectFollower.UpdateImmediate();
        }
    }
}
