using System;
using System.Collections;
using System.Collections.Generic;
using PlayFab;
using UnityEngine;
using UnityEngine.Serialization;


public class CameraFollower : MonoBehaviour
{
    private Transform _followObject;
    
    public Transform FollowObject
    {
        get => _followObject;
        set
        {
            _followObject = value;
            enabled = value != null;
        }
    }
    
    public Vector3 offset = new Vector3(0, 4f, 0);

    private void Awake()
    {
        enabled = false;
        
    }

    private void LateUpdate()
    {
        transform.position = FollowObject.position + offset + FollowObject.forward * 0.5f;
    }
    
}
