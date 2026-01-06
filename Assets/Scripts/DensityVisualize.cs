using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DensityVisualize : MonoBehaviour
{
    [Header("Shader Settings")]
    public ComputeShader pressureCS;
    public int fieldResolution = 256;
    public Renderer pressureQuadRenderer;  // assign the quad's MeshRenderer in Inspector
    


    RenderTexture fieldRT;
    ComputeBuffer posBuffer;
    int kernel;
    private float smoothedMaxAbsPressure = 1f; 
    public float maxAbsErrMultiplier = .35f;
    
    public void SetupPressureFieldGPU(int numParticles)
   {
       if (pressureCS == null) return;

       kernel = pressureCS.FindKernel("CSMain");

       if (fieldRT == null || fieldRT.width != fieldResolution)
       {
           if (fieldRT != null) fieldRT.Release();

           fieldRT = new RenderTexture(fieldResolution, fieldResolution, 0, RenderTextureFormat.ARGBFloat);
           fieldRT.enableRandomWrite = true;
           fieldRT.filterMode = FilterMode.Bilinear;
           fieldRT.wrapMode = TextureWrapMode.Clamp;
           fieldRT.Create();

           if (pressureQuadRenderer != null)
           {
               var mat = Application.isPlaying ? pressureQuadRenderer.material : pressureQuadRenderer.sharedMaterial;
               if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", fieldRT);
               else mat.mainTexture = fieldRT;
           }
       }


       if (posBuffer == null || posBuffer.count != numParticles)
       {
           posBuffer?.Release();
           posBuffer = new ComputeBuffer(numParticles, sizeof(float) * 2);
       }
   }

   public void UpdatePressureFieldGPU(Vector2[] positions, float[] densities, Vector2 boundsSize, float smoothingRadius, float targetDensity)
   {
       if (pressureCS == null || positions == null) return;
       SetupPressureFieldGPU(positions.Length);

       // Upload positions to GPU
       posBuffer.SetData(positions);

       // Set parameters
       pressureCS.SetTexture(kernel, "Result", fieldRT);
       pressureCS.SetBuffer(kernel, "Positions", posBuffer);
       pressureCS.SetInt("NumParticles", positions.Length);

       pressureCS.SetVector("BoundsSize", boundsSize);
       pressureCS.SetFloat("SmoothingRadius", smoothingRadius);
       pressureCS.SetFloat("TargetDensity", targetDensity);
       float maxAbsErr = EstimateMaxAbsNormalizedDensityError(targetDensity, positions.Length, densities);
       pressureCS.SetFloat("MaxAbsPressure", maxAbsErr * maxAbsErrMultiplier);


       pressureCS.SetVector("PosColor", (Vector4)(Color)new Color32(230, 30, 20, 255));
       pressureCS.SetVector("NegColor", (Vector4)(Color)new Color32(10, 80, 210, 255));
       pressureCS.SetVector("ZeroColor", (Vector4)(Color)new Color32(170, 210, 220, 255));


       int groups = Mathf.CeilToInt(fieldResolution / 8f);
       pressureCS.Dispatch(kernel, groups, groups, 1);
   }
   
   public float EstimateMaxAbsNormalizedDensityError(float targetDensity, int numParticles, float[] densities)
   {
       float maxAbs = 1e-6f;
       float invTarget = 1f / Mathf.Max(1e-6f, targetDensity);

       for (int i = 0; i < numParticles; i++)
       {
           float err = densities[i] * invTarget - 1f;
           maxAbs = Mathf.Max(maxAbs, Mathf.Abs(err));
       }
       return maxAbs;
   }
}
