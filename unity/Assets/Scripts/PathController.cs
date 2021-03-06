﻿using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/*********************************************************************/

// Created by Jam3 - http://www.jam3.com/

/*********************************************************************/

/*
 * Handles the creation of a path based on a series of points on the ground
 */

[RequireComponent(typeof(LineRenderer))]
public class PathController : MonoBehaviour
{
	// prefabs and linked scene objects
	public GameObject firstPointPrefab;
	public GameObject pointPrefab;
	public GameObject placementReticlePrefab;
	public GameObject calibrationReticlePrefab;
	public GameObject undoButtonPrefab;
	public GameObject placementUI;
	public GameObject calibrationUI;
	public EnvironmentController environmentController;
	public RobotController robotController;

    private ARController arController;
    private GameObject reticle;
	private GameObject calibrator;
	private List<Vector2> pathPoints;
	private List<GameObject> pathMarkers;
	private GameObject currentUndoButton;
	private Vector2 robotPosition;
	private float robotHeading;
	private Plane groundPlane;
	private float undoTime;

	private enum State
	{
		FindingFloor,
		Placing,
		Calibrating,
		Playing
	};
	private State state = State.FindingFloor;

	// simple Singleton implementation
	private static PathController _instance = null;
	public static PathController Instance {
		get { return _instance; }
	}

	public void Awake()
	{
		_instance = this;
	}

	public void Start()
	{
		// initilizations
		arController = FindObjectOfType<ARController> ();
		pathPoints = new List<Vector2> ();
		pathMarkers = new List<GameObject> ();
		state = State.FindingFloor;
		undoTime = 0.0f;
	}

	public void Update()
	{
		// find the ground plane and place a reticle on it - this will only be called once
		if (state == State.FindingFloor && arController.FirstGroundPlane != null) {
			groundPlane = new Plane (Vector3.up, new Vector3 (0, arController.FirstGroundPlane.Position.y, 0));
			state = State.Placing;
			reticle = Instantiate (placementReticlePrefab);
		}

        // handle taps for adding points to the path and for calibration
        if (Input.GetMouseButtonDown(0) && !PointerIsOverUI() && (undoTime - Time.fixedTime) < -0.2f)
        {
            switch (state)
            {
                // add a point to the path
                case State.Placing:
                    AddPoint(reticle.transform.position);
                    break;
                // calibrate
                case State.Calibrating:
                    CalibrateRobot(reticle.transform.position);
                    break;
            }
        }
    }

	public void LateUpdate()
	{
        // update the reticle's position on the ground
        if (state == State.Placing || state == State.Calibrating)
        {
            Transform cam = arController.m_firstPersonCamera.transform;
            Ray ray = new Ray(cam.position, cam.forward);
            float rayDistance;
            if (groundPlane.Raycast(ray, out rayDistance))
            {
                reticle.transform.position = ray.GetPoint(rayDistance);
                float tangoYaw = cam.rotation.eulerAngles.y;
                reticle.transform.rotation = Quaternion.Euler(0, tangoYaw, 0);
            }
        }
	}

    // calibrate the robot's position/orientation based on the calibrator object's transform
	private void CalibrateRobot(Vector3 position)
	{
		robotPosition = new Vector2 (position.x, position.z);
		robotHeading = arController.m_firstPersonCamera.transform.rotation.eulerAngles.y;
		Quaternion robotQuaternion = Quaternion.Euler (0, robotHeading, 0);
        if (calibrator == null) {
            calibrator = Instantiate(calibrationReticlePrefab, position, robotQuaternion);
        }
        calibrator.transform.position = position;
		calibrator.transform.rotation = robotQuaternion;
	}

	// add a point to the road path
	public void AddPoint(Vector3 pointPosition)
	{
		pathPoints.Add (new Vector2 (pointPosition.x, pointPosition.z));
		GameObject marker;
		if (pathMarkers.Count == 0) {
			marker = firstPointPrefab;
		} else {
			marker = pointPrefab;
		}
		pathMarkers.Add (Instantiate (marker, pointPosition, Quaternion.identity));
		if (pathMarkers.Count == 1) {
			Animation anim = pathMarkers [0].GetComponent<Animation> ();
			anim.Play ("animIn");
			anim.PlayQueued ("idle", QueueMode.CompleteOthers);
			pointPosition.y += 0.3f;
		}
		if (currentUndoButton) {
			Destroy (currentUndoButton);
		}
		currentUndoButton = Instantiate (undoButtonPrefab, pointPosition, Quaternion.identity);
		UpdateLineRenderer ();
	}

	// update the line renderer visualizing the path
	private void UpdateLineRenderer()
	{
		LineRenderer renderer = GetComponent<LineRenderer> ();
		int pointCount = pathMarkers.Count;
		Button button = placementUI.transform.GetChild (0).GetComponent<Button> ();

		// a line renderer must have at least 2 points
		if (pointCount < 2) {
			renderer.enabled = false;
			button.interactable = false;
			return;
		}

		renderer.enabled = true;
		button.interactable = true;
		renderer.positionCount = pointCount;
		for (int i = 0; i < pointCount; i++) {
			renderer.SetPosition (i, pathMarkers [i].transform.position);
		}
	}

	// delete the last point in the path
	public void Undo()
	{
		undoTime = Time.fixedTime;
		if (pathPoints.Count == 0) {
			return;
		}
		int lastIndex = pathPoints.Count - 1;
		pathPoints.RemoveAt (lastIndex);
		Destroy (pathMarkers [lastIndex]);
		if (currentUndoButton) {
			Destroy (currentUndoButton);
			if (lastIndex > 0) {
				Vector3 pos = pathMarkers [lastIndex - 1].transform.position;
				currentUndoButton = Instantiate (undoButtonPrefab, pos, Quaternion.identity);
				if (lastIndex == 1) {
					currentUndoButton.transform.position = new Vector3 (pos.x, pos.y + 0.3f, pos.z);
				}
			}
		}

		pathMarkers.RemoveAt (lastIndex);
		UpdateLineRenderer ();
	}

    // return the list of path points with the robot's added position as the first point
	public List<Vector2> GetPathPoints()
	{
		List<Vector2> clone = new List<Vector2> (pathPoints);
		clone.Insert (0, robotPosition);
		return clone;
	}

	public float GetInitialHeading()
	{
		return robotHeading + 90f;
	}

	public void Reset()
	{
		while (pathPoints.Count > 0) {
			Undo ();
		}
	}

	// finish placing roads
	public void FinishPlacing()
	{
        if (state != State.Placing) return;

		state = State.Calibrating;
		Destroy (reticle);
		reticle = Instantiate (calibrationReticlePrefab);
		placementUI.SetActive (false);
		calibrationUI.SetActive (true);
		Destroy (currentUndoButton);
	}

	// finish calibration
	public void FinishCalibration()
	{
        if (state != State.Calibrating) return;

		state = State.Playing;

        // deactivate items
		foreach (GameObject marker in pathMarkers) {
			marker.SetActive (false);
		}
        LineRenderer lineRenderer = GetComponent<LineRenderer> ();
		lineRenderer.enabled = false;
		calibrationUI.SetActive (false);
        Destroy (reticle);
		Destroy (calibrator);
        environmentController.GenerateEnvironment ();
        robotController.StartRobot ();
    }

    // check we are touching the UI
	private bool PointerIsOverUI()
	{
		PointerEventData eventDataCurrentPosition = new PointerEventData (EventSystem.current);
		eventDataCurrentPosition.position = new Vector2 (Input.mousePosition.x, Input.mousePosition.y);
		List<RaycastResult> results = new List<RaycastResult> ();
		EventSystem.current.RaycastAll (eventDataCurrentPosition, results);
		return results.Count > 0;
	}

    // returns the height of the ground plane in the Unity world
	public float FloorHeight()
	{
		if(arController.FirstGroundPlane != null) return arController.FirstGroundPlane.Position.y;
		else return -1.0f;
	}
}
