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
        [SerializeField]
        private Transform head;
        
        /// <summary>
        /// The head object follower, added to the GameObject by `Awake`.
        /// </summary>
        private ObjectFollower headObjectFollower;
        
        static public DeviceTracking Instance { get; private set; }
        
        public Transform TrackingOrigin => trackingOrigin;


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
            headObjectFollower.moment = ObjectFollower.Moment.OnFixedUpdate;

            // Set the head object follower source.
            if (null == head)
            {
                Debug.LogError($"Head object not specified.");
                return;
            }
            headObjectFollower.source = head;
            
            
        }
        

        /// <summary>
        /// Adds an object to follow the head.
        /// </summary>
        /// <param name="target">The object to follow the head.</param>
        public void AddHeadFollower(Transform target)
        {
            headObjectFollower.AddTarget(target);
        }
    }
}
