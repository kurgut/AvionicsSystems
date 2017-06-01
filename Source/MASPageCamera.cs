﻿/*****************************************************************************
 * The MIT License (MIT)
 * 
 * Copyright (c) 2017 MOARdV
 * 
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to
 * deal in the Software without restriction, including without limitation the
 * rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
 * sell copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 * 
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
 * DEALINGS IN THE SOFTWARE.
 * 
 ****************************************************************************/
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace AvionicsSystems
{
    internal class MASPageCamera : IMASMonitorComponent
    {
        private string name = "(anonymous)";
        private GameObject imageObject;
        private Material imageMaterial;
        private Material postProcShader = null;
        private MeshRenderer meshRenderer;
        private RenderTexture cameraTexture;
        private string variableName;
        private MASFlightComputer.Variable range1, range2;
        string[] propertyValue = new string[0];
        Action<double>[] propertyCallback = new Action<double>[0];
        private readonly bool rangeMode;
        private MASFlightComputer.Variable cameraSelector;
        private MASCamera activeCamera = null;
        private MASFlightComputer comp;
        private bool currentState;
        private bool coroutineActive;

        internal MASPageCamera(ConfigNode config, InternalProp prop, MASFlightComputer comp, MASMonitor monitor, Transform pageRoot, float depth)
        {
            this.comp = comp;
            if (!config.TryGetValue("name", ref name))
            {
                name = "(anonymous)";
            }

            Vector2 position = Vector2.zero;
            if (!config.TryGetValue("position", ref position))
            {
                throw new ArgumentException("Unable to find 'position' in CAMERA " + name);
            }

            Vector2 size = Vector2.zero;
            if (!config.TryGetValue("size", ref size))
            {
                throw new ArgumentException("Unable to find 'size' in CAMERA " + name);
            }

            cameraTexture = new RenderTexture((int)size.x, (int)size.y, 24, RenderTextureFormat.ARGB32);

            string cameraName = string.Empty;
            if (config.TryGetValue("camera", ref cameraName))
            {
                cameraName = cameraName.Trim();
            }
            else
            {
                throw new ArgumentException("Unable to find 'cameraName' in CAMERA " + name);
            }

            if (config.TryGetValue("variable", ref variableName))
            {
                variableName = variableName.Trim();
            }

            string range = string.Empty;
            if (config.TryGetValue("range", ref range))
            {
                string[] ranges = range.Split(',');
                if (ranges.Length != 2)
                {
                    throw new ArgumentException("Incorrect number of values in 'range' in CAMERA " + name);
                }
                range1 = comp.GetVariable(ranges[0], prop);
                range2 = comp.GetVariable(ranges[1], prop);

                rangeMode = true;
            }
            else
            {
                rangeMode = false;
            }

            string shaderName = string.Empty;
            string[] propertyName = new string[0];
            if (config.TryGetValue("shader", ref shaderName))
            {
                shaderName = shaderName.Trim();
                if (!MASLoader.shaders.ContainsKey(shaderName))
                {
                    throw new ArgumentException("Unknown 'shader' in CAMERA " + name);
                }

                postProcShader = new Material(MASLoader.shaders[shaderName]);
                if (postProcShader == null)
                {
                    throw new ArgumentException("Failed to load 'shader' in CAMERA " + name);
                }

                string concatProperties = string.Empty;
                if (config.TryGetValue("properties", ref concatProperties))
                {
                    string[] propertiesList = concatProperties.Split(';');
                    int listLength = propertiesList.Length;
                    if (listLength > 0)
                    {
                        propertyName = new string[listLength];
                        propertyValue = new string[listLength];
                        propertyCallback = new Action<double>[listLength];

                        for (int i = 0; i < listLength; ++i)
                        {
                            string[] pair = propertiesList[i].Split(':');
                            if (pair.Length != 2)
                            {
                                throw new ArgumentOutOfRangeException("Incorrect number of parameters for property: requires 2, found " + pair.Length + " in property " + propertiesList[i] + " for CAMERA " + name);
                            }
                            propertyName[i] = pair[0].Trim();
                            propertyValue[i] = pair[1].Trim();
                        }
                    }
                }
            }

            imageObject = new GameObject();
            imageObject.name = pageRoot.gameObject.name + "-MASPageImage-" + name + "-" + depth.ToString();
            imageObject.layer = pageRoot.gameObject.layer;
            imageObject.transform.parent = pageRoot;
            imageObject.transform.position = pageRoot.position;
            imageObject.transform.Translate(monitor.screenSize.x * -0.5f + position.x, monitor.screenSize.y * 0.5f - position.y, depth);
            // add renderer stuff
            MeshFilter meshFilter = imageObject.AddComponent<MeshFilter>();
            meshRenderer = imageObject.AddComponent<MeshRenderer>();
            Mesh mesh = new Mesh();
            mesh.vertices = new[]
                {
                    new Vector3(0.0f, 0.0f, depth),
                    new Vector3(size.x, 0.0f, depth),
                    new Vector3(0.0f, -size.y, depth),
                    new Vector3(size.x, -size.y, depth),
                };
            mesh.uv = new[]
                {
                    new Vector2(0.0f, 1.0f),
                    Vector2.one,
                    Vector2.zero,
                    new Vector2(1.0f, 0.0f),
                };
            mesh.triangles = new[] 
                {
                    0, 1, 2,
                    1, 3, 2
                };
            mesh.RecalculateBounds();
            mesh.Optimize();
            mesh.UploadMeshData(true);
            meshFilter.mesh = mesh;

            imageMaterial = new Material(Shader.Find("KSP/Alpha/Unlit Transparent"));
            imageMaterial.mainTexture = cameraTexture;
            meshRenderer.material = imageMaterial;

            if (!string.IsNullOrEmpty(variableName))
            {
                currentState = false;
                // Disable the mesh if we're in variable mode
                imageObject.SetActive(false);
                comp.RegisterNumericVariable(variableName, prop, VariableCallback);
            }
            else
            {
                currentState = true;
                imageObject.SetActive(true);
            }

            cameraSelector = comp.RegisterOnVariableChange(cameraName, prop, CameraSelectCallback);
            CameraSelectCallback();

            for (int i = 0; i < propertyValue.Length; ++i)
            {
                int propertyId = Shader.PropertyToID(propertyName[i]);
                propertyCallback[i] = delegate(double a) { PropertyCallback(propertyId, a); };
                comp.RegisterNumericVariable(propertyValue[i], prop, propertyCallback[i]);
            }

            if (coroutineActive == false)
            {
                comp.StartCoroutine(CameraRenderCoroutine());
            }
        }

        /// <summary>
        /// Callback to update the shader's properties.
        /// </summary>
        /// <param name="propertyId">The property ID to update.</param>
        /// <param name="newValue">The new value for that property.</param>
        private void PropertyCallback(int propertyId, double newValue)
        {
            postProcShader.SetFloat(propertyId, (float)newValue);
        }

        /// <summary>
        /// Handle a changed value
        /// </summary>
        /// <param name="newValue"></param>
        private void VariableCallback(double newValue)
        {
            if (rangeMode)
            {
                newValue = (newValue.Between(range1.SafeValue(), range2.SafeValue())) ? 1.0 : 0.0;
            }

            bool newState = (newValue > 0.0);

            if (newState != currentState)
            {
                currentState = newState;
                imageObject.SetActive(currentState);

                if (currentState == false)
                {
                    if (cameraTexture.IsCreated())
                    {
                        cameraTexture.Release();
                    }
                }
            }
        }

        /// <summary>
        /// Callback used to select active cameras
        /// </summary>
        private void CameraSelectCallback()
        {
            try
            {
                // This can return null at startup when the VC hasn't had a chance
                // to run.  Unfortunately, that means we have to call this callback
                // every time the camera should be enabled.
                activeCamera = comp.vc.FindCameraModule(cameraSelector.String());
            }
            catch
            {
                activeCamera = null;
            }
        }

        /// <summary>
        /// Coroutine for rendering the active camera.
        /// </summary>
        /// <returns></returns>
        private IEnumerator CameraRenderCoroutine()
        {
            coroutineActive = true;

            while (this.comp != null)
            {
                yield return new WaitForFixedUpdate();

                if (currentState == true)
                {
                    if (!cameraTexture.IsCreated())
                    {
                        cameraTexture.Create();
                    }
                    if (activeCamera == null)
                    {
                        CameraSelectCallback();

                        cameraTexture.DiscardContents();
                        RenderTexture backup = RenderTexture.active;
                        RenderTexture.active = cameraTexture;
                        GL.Clear(true, true, new Color(1.0f, 0.0f, 0.0f));
                        RenderTexture.active = backup;
                    }
                    else
                    {
                        cameraTexture.DiscardContents();
                        if (postProcShader == null)
                        {
                            activeCamera.Render(cameraTexture);
                        }
                        else
                        {
                            RenderTexture targetTexture = RenderTexture.GetTemporary(cameraTexture.width, cameraTexture.height, cameraTexture.depth, cameraTexture.format);
                            targetTexture.DiscardContents(); // needed?
                            activeCamera.Render(targetTexture);
                            Graphics.Blit(targetTexture, cameraTexture, postProcShader);
                            RenderTexture.ReleaseTemporary(targetTexture);
                        }
                    }
                }
                else
                {
                    if (cameraTexture.IsCreated())
                    {
                        cameraTexture.Release();
                    }
                }
            }

            coroutineActive = false;
        }

        /// <summary>
        /// Enable / disable renderer components without disabling game objects.
        /// </summary>
        /// <param name="enable"></param>
        public void EnableRender(bool enable)
        {
            meshRenderer.enabled = enable;
        }

        /// <summary>
        ///  Return the name of the action.
        /// </summary>
        /// <returns></returns>
        public string Name()
        {
            return name;
        }

        /// <summary>
        /// Release resources
        /// </summary>
        public void ReleaseResources(MASFlightComputer comp, InternalProp internalProp)
        {
            this.comp = null;
            activeCamera = null;

            if (cameraTexture.IsCreated())
            {
                cameraTexture.Release();
            }
            UnityEngine.GameObject.Destroy(cameraTexture);
            cameraTexture = null;

            if (postProcShader != null)
            {
                UnityEngine.GameObject.Destroy(postProcShader);
                postProcShader = null;
            }
            UnityEngine.GameObject.Destroy(imageObject);
            imageObject = null;
            UnityEngine.GameObject.Destroy(imageMaterial);
            imageMaterial = null;

            if (!string.IsNullOrEmpty(variableName))
            {
                comp.UnregisterNumericVariable(variableName, internalProp, VariableCallback);
            }

            for (int i = 0; i < propertyValue.Length; ++i)
            {
                comp.UnregisterNumericVariable(propertyValue[i], internalProp, propertyCallback[i]);
            }

            if (!string.IsNullOrEmpty(cameraSelector.name))
            {
                comp.UnregisterOnVariableChange(cameraSelector.name, internalProp, CameraSelectCallback);
            }
        }
    }
}
