using System;
using System.Collections;
using System.Collections.Generic;
using System.IO.IsolatedStorage;
using System.Threading.Tasks;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Serialization;
using static UnityEngine.Mathf;
using Random = UnityEngine.Random;
using UnityEngine.Rendering;


[ExecuteAlways]
public class TestGridAndParticles : MonoBehaviour
{ 
    
    // Start is called before the first frame update
    public CircleRenderer renderer;
    
    [Header("Particle Properties")]
    public float particleSize = 1f;
    [Range(0,1)] public float collisionDamping = 1f;
    public float gravity;
    public Vector2 boundsSize;
    public int numParticles = 100;
    public float particleSpacing = 0.1f;
    public float smoothingRadius = 1f;
    public float mass = 1;
    public float targetDensity = 1f;
    public float pressureMultiplier = 1f;
    public Color particleColor;
    
    public DensityVisualize densityVisualize;
    
    private static Color lightBlue = new Color32(4, 144, 251, 255);
    public Vector2[] positions;
    public Vector2[] velocities;
    private float[] particleProperties;
    private float[] densities;
    private System.Random rng = new System.Random();

    private int[] startIndices;
    private Entry[] spatialLookup;
    private (int offssetX, int offsetY)[] cellOffsets;
 

    struct Entry : IComparable<Entry>
    { 
        public int index;
        public uint key;

        public Entry(int i, uint k)
        {
            index = i;
            key = k;
        }

        public int CompareTo(Entry other)
        {
            return key.CompareTo(other.key);
        }
    }


    void Start()
    {
        positions = new Vector2[numParticles];
        velocities = new Vector2[numParticles];
        densities = new float[numParticles];
        startIndices = new int[numParticles];
        spatialLookup = new Entry[numParticles];
        cellOffsets = new (int, int)[9];
        for (int i = 0; i < 2; i++)
        {
            for (int j = 0; j < 2; j++)
            {
                cellOffsets[i * 3 + j] = (i-1, j-1);
            }
        }
        SpawnParticlesRandom();

    }

    void OnEnable() => Start();

    void OnValidate()
    {
        if (!Application.isPlaying)
        {
            Start();
            for (int i = 0; i < numParticles; i++)
            {
                renderer.DrawCircle(positions[i], particleSize, particleColor);
            }

            // densityVisualize.UpdatePressureFieldGPU(positions, densities, boundsSize, smoothingRadius, targetDensity);
            renderer.DrawRectOutline(Vector2.zero, boundsSize, 0.01f, Color.green);
        }
    }
    


    // Update is called once per frame
    void Update()
    {
        if (!Application.isPlaying)
        {
            for (int i = 0; i < numParticles; i++)
            {
                renderer.DrawCircle(positions[i], particleSize, particleColor);
            }
            renderer.DrawRectOutline(Vector2.zero, boundsSize, 0.01f, Color.green);
            return;
        }
        SimulationStep(Time.deltaTime);

        // densityVisualize.UpdatePressureFieldGPU(positions, densities, boundsSize, smoothingRadius, targetDensity);
        for (int i = 0; i < numParticles; i++)
        {
            renderer.DrawCircle(positions[i], particleSize, particleColor);
        }
        
        renderer.DrawRectOutline(Vector2.zero, boundsSize, 0.01f, Color.green);
    }
    
    void SpawnParticlesRandom()
    {
        for (int i = 0; i < numParticles; i++)
        {
            float x = (float)rng.NextDouble() * boundsSize.x - boundsSize.x * .5f;
            float y = (float)rng.NextDouble() * boundsSize.y - boundsSize.y * .5f;
            positions[i] = new Vector2(x, y);
        }
    }

    void SpawnParticlesGrid()
    {
        int numParticlesPerRow = (int)Sqrt(numParticles);
        int numParticlesPerCol = (numParticles -1 ) / numParticlesPerRow + 1;
        float spacing = particleSize * 2 + particleSpacing;
        for (int i = 0; i < numParticles; i++)
        {
            float x = (i % numParticlesPerRow - numParticlesPerRow * .5f + .5f) * spacing;
            float y = (i / numParticlesPerRow - numParticlesPerCol * .5f + .5f) * spacing;
            positions[i] = new Vector2(x, y);
        }
    }


    void SimulationStep(float deltaTime)
    {
        //apply gravity and calculate densities
        Parallel.For(0, numParticles, i =>
        {
            velocities[i] += gravity * deltaTime * Vector2.down;
        });
        UpdateSpatialLookup();
        
        //calculate densities
        Parallel.For(0, numParticles, i =>
        {
            densities[i] = CalculateDensity(positions[i]);
        });
        
        
        // Calculate and apply pressure forces
        Parallel.For(0, numParticles, i =>
        {
            Vector2 pressureForce = CalculatePressureForce(i);
            Vector2 pressureAcceleration = pressureForce / densities[i];
            velocities[i] += pressureAcceleration * deltaTime;
        });
        
        //update positions and resolve collisions
        Parallel.For(0, numParticles, i =>
        {
            positions[i] += velocities[i] * deltaTime;
            ResolveCollisions(ref positions[i], ref velocities[i]);
        });
    }
    void ResolveCollisions(ref Vector2 position, ref Vector2 velocity)
    {
        Vector2 halfBoundsSize = boundsSize / 2 - Vector2.one * particleSize;
        if (Abs(position.x) >= halfBoundsSize.x)
        {
            position.x = halfBoundsSize.x * Sign(position.x);
            velocity.x *= -1 * collisionDamping;
        }

        if (Abs(position.y) >= halfBoundsSize.y)
        {
            position.y = halfBoundsSize.y * Sign(position.y);
            velocity.y *= -1 * collisionDamping;
        }
    }
    
    /// <summary>
    /// O(1)
    /// </summary>
    static float SmoothingKernel(float smoothingRadius, float dist)
    {
        if(dist >= smoothingRadius) return 0;
        
        float volume = (PI * Pow(smoothingRadius, 4)) / 6;
        return (smoothingRadius - dist) * (smoothingRadius - dist) / volume;
    }


    static float SmoothingKernelDerivative(float smoothingRadius, float dist)
    {
        if (dist >= smoothingRadius) return 0f;
        
        float scale = 12 / (Pow(smoothingRadius, 4) * PI);
        return (dist - smoothingRadius) * scale;
    }

    
    /// <summary>
    /// O(n)
    /// </summary>
    float CalculateDensity(Vector2 point)
    {
        float density = 0;
        ArrayList particleIndices = ForeachPointWithinRadius(point);
        foreach (int ind in particleIndices)
        {
            Vector2 pos = positions[ind];
            float dist = (pos - point).magnitude;
            float influence = SmoothingKernel(smoothingRadius, dist);
            density += influence * mass;
        }
        return density;
    }

    


    /// <summary>
    /// O(1)
    /// </summary>
    Vector2 CalculatePressureForce(int particleIndex)
    {
        Vector2 pressureForce = Vector2.zero;
        float currentDensity = densities[particleIndex];
        ArrayList particleIndices = ForeachPointWithinRadius(positions[particleIndex]);
        foreach(int i in particleIndices)
        {
            if(i == particleIndex) continue;
            
            Vector2 offset = positions[particleIndex] - positions[i];
            float dist = offset.magnitude;
            Vector2 dir = dist == 0 ? GetRandomDir() : offset / dist;
            
            float slope = SmoothingKernelDerivative(smoothingRadius, dist);
            float density = densities[i];
            float sharedPressure = CalculateSharedPressure(density, currentDensity);
            pressureForce += -sharedPressure * slope * mass / density * dir;
        }
        return pressureForce;
    }

    Vector2 GetRandomDir()
    {
        float x = (float)rng.NextDouble() * 2 -1;
        float y = (float)rng.NextDouble() * 2 -1;
        return new Vector2(x, y).normalized;
    }
/// <summary>
/// O(1)
/// </summary>

    float ConvertDensityToPressure(float density)
    {
        float densityError = density - targetDensity;
        float pressure = densityError * pressureMultiplier;
        return pressure;
    }

    float CalculateSharedPressure(float densityA, float densityB)
    {
        float pressureA = ConvertDensityToPressure(densityA);
        float pressureB = ConvertDensityToPressure(densityB);
        return (pressureA + pressureB) / 2;
    }
    
    
    // Converts a point position to the coordinate of the cell it is in
    public (int x, int y) PositionToCellCoord(Vector2 point, float radius)
    {
        int cellx = (int)(point.x / radius);
        int celly = (int)(point.y / radius);
        return (cellx, celly);
    }

    public uint HashCell(int cellx, int celly)
    {
        uint a = (uint)cellx * 15823;
        uint b = (uint)celly * 9737333;
        return a + b;
    }

    public uint GetKeyFromHash(uint hash)
    {
        return hash % (uint)spatialLookup.Length;
    }

    public void UpdateSpatialLookup()
    {
        Parallel.For(0, numParticles, i =>
        {
            (int cellx, int celly) = PositionToCellCoord(positions[i], smoothingRadius);
            uint cellKey = GetKeyFromHash(HashCell(cellx, celly));
            spatialLookup[i] = new Entry(i, cellKey);
            startIndices[i] = int.MaxValue;
        });
        
        Array.Sort(spatialLookup);

        Parallel.For(0, numParticles, i =>
        {
            uint key = spatialLookup[i].key;
            uint keyPrev = i==0 ? uint.MaxValue : spatialLookup[i-1].key;
            if(key != keyPrev) startIndices[key] = i;
        });
    }

    
    public ArrayList ForeachPointWithinRadius(Vector2 centerPoint)
    {
        (int centerX, int centerY) = PositionToCellCoord(centerPoint, smoothingRadius);
        float sqrRadius = smoothingRadius * smoothingRadius;
        ArrayList particleIndicesInsideRadius = new ArrayList();
        foreach ((int offsetX, int offsetY) in cellOffsets)
        {
            uint key = GetKeyFromHash(HashCell(centerX + offsetX, centerY + offsetY));
            int cellStartIndex = startIndices[key];

            for (int i = cellStartIndex; i < spatialLookup.Length; i++)
            {
                if (spatialLookup[i].key != key) break;

                int particleIndex = spatialLookup[i].index;
                float sqrDist = (positions[particleIndex] - centerPoint).sqrMagnitude;

                if (sqrDist <= sqrRadius)
                {
                    particleIndicesInsideRadius.Add(particleIndex);
                }
            }
        }

        return particleIndicesInsideRadius;
    }
    
    
    
    
   //compute shader to visualize density
   
   
   
/* Depreciated Functions
    void CreateParticles(int seed)
    {
        rng = new System.Random(seed);
        positions = new Vector2[numParticles];
        particleProperties = new float[numParticles];
        for (int i = 0; i < numParticles; i++)
        {
            float x = (float) (rng.NextDouble() - .5) * boundsSize.x;
            float y = (float) (rng.NextDouble() - .5) * boundsSize.y;
            positions[i] = new Vector2(x, y);
            particleProperties[i] = ExampleFunction(positions[i]);
        }
    }
/// <summary>
/// O(n)
/// </summary>
    float CalculateProperty(Vector2 point)
    {
        float property = 0;

        for (int i = 0; i < numParticles; i++)
        {
            float dist = (positions[i] - point).magnitude;
            float influence = SmoothingKernel(smoothingRadius, dist);
            float density = densities[i];
            property += influence * mass * particleProperties[i] / density;
        }
        return property;
    }
    
    float ExampleFunction(Vector2 pos)
    {
        return Cos(pos.y - 3 + Sin(pos.x));
    }
*/
}
