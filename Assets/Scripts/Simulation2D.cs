using System;
using System.Collections;
using System.Collections.Generic;
using System.IO.IsolatedStorage;
using System.Threading.Tasks;
using Unity.Mathematics;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Serialization;
using static UnityEngine.Mathf;
using Random = UnityEngine.Random;
using UnityEngine.Rendering;


[ExecuteAlways]
public class DrawParticle : MonoBehaviour
{ 
    
    // Start is called before the first frame update
    public CircleRenderer renderer;
    
    public float particleSize = .05f;
    public float particleSpacing = 0.04f;
    
    [Header("Particle Properties")]
    public int numParticles = 100;
    public float smoothingRadius = 1f;
    public float targetDensity = 1f;
    public float pressureMultiplier = 1f;
    public float gravity;
    [Range(0,1)] public float collisionDamping = 1f;
    public Vector2 boundsSize;
    public float mass = 1;
    public Gradient colorGradient;
    
    [Header("Walls")]
    public float wallStiffness = 40f;   // how hard the wall pushes
    public float wallDamping   = 5f;    // damp normal velocity near the wall
    public float wallFriction  = 1f;    // damp tangential velocity near the wall

    private Color particleColor;
    private DensityVisualize densityVisualize;
    public ParticleSpawner particleSpawner;
    
    private static Color lightBlue = new Color32(4, 144, 251, 255);
    
    //System helpers and Variables
    private System.Random rng = new System.Random();
    private const float deltaTime = 1 / 60f;
    
    [Header("Mouse Interaction")]
    public float mouseInteractionRadius = 1f;
    public float mouseStrength = 10f;
    public Vector2 mousePos;
    
    //Particle Data
    public Vector2[] positions;
    public Vector2[] velocities;
    private float[] densities;
    private Vector2[] predictedPositions;
    private float velocityMax;
    public Vector2[][] boundaryPositions;
    public Vector2[] boundaryPositionsX;
    public Vector2[] boundaryPositionsY;
    private Vector2 boundaryParticlesPerSide;
    public float boundaryParticleSpacingMultiplier;
    
    
    //Spatial Lookup Helpers
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

    struct ParticleSpawnData
    {
        public float2[] positions;
        public float2[] velocities;

        public ParticleSpawnData(int num)
        {
            positions = new float2[num];
            velocities = new float2[num];
        }
    }


    void Start()
    {
        positions = new Vector2[numParticles];
        velocities = new Vector2[numParticles];
        densities = new float[numParticles];
        startIndices = new int[numParticles];
        spatialLookup = new Entry[numParticles];
        predictedPositions = new Vector2[numParticles];
        boundaryParticlesPerSide = new Vector2(boundsSize.x / (particleSize + particleSize * boundaryParticleSpacingMultiplier), boundsSize.y / (particleSize + particleSize * boundaryParticleSpacingMultiplier));
        boundaryPositions = new Vector2[2][];
        boundaryPositions[0] = new Vector2[CeilToInt(boundaryParticlesPerSide.x * 2)];
        boundaryPositions[1] = new Vector2[CeilToInt(boundaryParticlesPerSide.y * 2)];
        CreateBoundaryParticles(particleSize + particleSize * boundaryParticleSpacingMultiplier);
        boundaryPositionsX = boundaryPositions[0];
        boundaryPositionsY = boundaryPositions[1];
        cellOffsets = new (int, int)[9];
        int idx = 0;
        for (int ox = -1; ox <= 1; ox++)
        for (int oy = -1; oy <= 1; oy++)
            cellOffsets[idx++] = (ox, oy);
        ParticleSpawner.ParticleSpawnData spawnData = particleSpawner.GetSpawnData();
        Parallel.For(0, numParticles, i =>
        {
            positions[i] = spawnData.positions[i];
            velocities[i] = spawnData.velocities[i];
        });
        
        
        
    }

    void OnEnable() => Start();

    void OnValidate()
    {
        if (!Application.isPlaying)
        {
            Start();
            for (int i = 0; i < numParticles; i++)
            {
                renderer.DrawCircle(positions[i], particleSize, colorGradient.Evaluate(0));
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
            Parallel.For(0, numParticles, i => velocityMax = Max(velocityMax, velocities[i].magnitude));
            for (int i = 0; i < numParticles; i++)
            {
                renderer.DrawCircle(positions[i], particleSize, colorGradient.Evaluate(0));
            }
            renderer.DrawRectOutline(Vector2.zero, boundsSize, 0.01f, Color.green);
            return;
        }
        
        SimulationStep(1/120f);
        mousePos = Input.mousePosition;
        // densityVisualize.UpdatePressureFieldGPU(positions, densities, boundsSize, smoothingRadius, targetDensity);
        renderer.DrawRectOutline(Vector2.zero, boundsSize, 0.01f, Color.green);
        
        for (int i = 0; i < numParticles; i++)
        {
            renderer.DrawCircle(positions[i], particleSize, colorGradient.Evaluate(velocities[i].magnitude));
            
        }
        
        
    }
    void CreateBoundaryParticles(float particleSize)
    {
        float xSize = boundsSize.x / 2;
        float ySize = boundsSize.y / 2;
        for (int i = 0; i < boundaryParticlesPerSide.x; i++)
        {
            boundaryPositions[0][i] = new Vector2(-xSize + particleSize * i, -ySize);
            boundaryPositions[0][(int)(i + boundaryParticlesPerSide.x)] = new Vector2(-xSize + particleSize * i, ySize);
        }

        for (int i =0; i < boundaryParticlesPerSide.y; i++)
        {
            boundaryPositions[1][i] = new Vector2(-xSize, -ySize + particleSize * i);
            boundaryPositions[1][(int)(i + boundaryParticlesPerSide.y)] = new Vector2(xSize, -ySize + particleSize * i);
        }
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
        int numParticlesPerCol = (numParticles - 1) / numParticlesPerRow + 1;
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
        Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);

        
        //apply gravity and calculate densities
        Parallel.For(0, numParticles, i => {
            velocities[i] += gravity * deltaTime * Vector2.down;
            predictedPositions[i] = positions[i] + velocities[i] * 1/120f;
        });
        
        UpdateSpatialLookup(predictedPositions);
        
        //calculate densities
        Parallel.For(0, numParticles, i =>
        {
            densities[i] = CalculateDensity(predictedPositions[i], predictedPositions);
        });
        
        
        float interactionStrength = 0;
        if(Input.GetKey(KeyCode.Mouse0)) interactionStrength = .5f * mouseStrength;
        else if(Input.GetKey(KeyCode.Mouse1)) interactionStrength = -1f * mouseStrength;
        
        // Calculate and apply pressure forces
        Parallel.For(0, numParticles, i =>
        {
            Vector2 pressureForce = CalculatePressureForce(i, predictedPositions);
            Vector2 pressureAcceleration = pressureForce / densities[i];
            if (interactionStrength != 0) {
                pressureAcceleration += InteractionForce(mousePos, mouseInteractionRadius, interactionStrength, i);
            }

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

    Vector2 InteractionForce(Vector2 inputPos, float radius, float strength, int particleIndex)
    {
        Vector2 interactionForce = Vector2.zero;
        strength *=pressureMultiplier;
        Vector2 offset = inputPos - positions[particleIndex];
        float sqrDst = Vector2.Dot(offset, offset);
        
        if (sqrDst < radius * radius)
        {
            float dst = Sqrt(sqrDst);
            Vector2 dirToInputPoint = dst <= float.Epsilon ? Vector2.zero : offset / dst;
            float centerT = 1 - dst / radius;
            interactionForce += (dirToInputPoint  * strength - velocities[particleIndex]) * centerT;
        }
        return interactionForce;
    }
    
    /// <summary>
    /// O(1)
    /// </summary>
    static float SmoothingKernel(float smoothingRadius, float dist)
    {
        if(dist >= smoothingRadius) return 0;
        
        float volume = 10 / (PI * Pow(smoothingRadius, 5));
        return Pow(smoothingRadius - dist, 3) * volume;
    }


    static float SmoothingKernelDerivative(float smoothingRadius, float dist)
    {
        if (dist >= smoothingRadius) return 0f;
        
        float scale = -30 / (Pow(smoothingRadius, 5) * PI);
        return (smoothingRadius - dist) * (smoothingRadius - dist) * scale;
    }

    
    /// <summary>
    /// O(n)
    /// </summary>
    float CalculateDensity(Vector2 centerPoint, Vector2[] points)
    {
        (int centerX, int centerY) = PositionToCellCoord(centerPoint, smoothingRadius);
        float sqrRadius = smoothingRadius * smoothingRadius;
        int idx = 0;
        float density = 0;
        foreach ((int offsetX, int offsetY) in cellOffsets)
        {
            uint key = GetKeyFromHash(HashCell(centerX + offsetX, centerY + offsetY));
            int cellStartIndex = startIndices[key];
            for (int i = cellStartIndex; i < spatialLookup.Length; i++)
            {
                if (spatialLookup[i].key != key) break;
                
                int particleIndex = spatialLookup[i].index;
                
                Vector2 offset = points[particleIndex] - centerPoint;
                float sqrDist = offset.sqrMagnitude;

                if (sqrDist <= sqrRadius)
                {
                    float dist = offset.magnitude;
                    float influence = SmoothingKernel(smoothingRadius, dist); 
                    density += influence * mass;
                }
            }
        }
        return density;
    }
    
   


    
    
    

    /// <summary>
    /// O(1)
    /// </summary>
    Vector2 CalculatePressureForce(int centerIndex, Vector2[] points)
    {
        Vector2 centerPoint = points[centerIndex];
        (int centerX, int centerY) = PositionToCellCoord(centerPoint, smoothingRadius);
        float sqrRadius = smoothingRadius * smoothingRadius;
        Vector2 pressureForce = Vector2.zero;
        foreach ((int offsetX, int offsetY) in cellOffsets)
        {
            uint key = GetKeyFromHash(HashCell(centerX + offsetX, centerY + offsetY));
            int cellStartIndex = startIndices[key];
            for (int i = cellStartIndex; i < spatialLookup.Length; i++)
            {
                if (spatialLookup[i].key != key) break;
                int particleIndex = spatialLookup[i].index;
                if (particleIndex == centerIndex) continue;
                
                Vector2 offset = points[particleIndex] - centerPoint;
                float sqrDist = offset.sqrMagnitude;

                if (sqrDist <= sqrRadius)
                {
                    float currentDensity = densities[centerIndex];
                    float dist = offset.magnitude;
                    Vector2 dir = dist == 0 ? GetRandomDir() : offset / dist;
                    float slope = SmoothingKernelDerivative(smoothingRadius, dist);
                    float density = densities[particleIndex];
                    float sharedPressure = CalculateSharedPressure(density, currentDensity);
                    pressureForce += sharedPressure * slope * mass / density * dir;
                }
            }
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

    public void UpdateSpatialLookup(Vector2[] points)
    {
        Parallel.For(0, numParticles, i =>
        {
            (int cellx, int celly) = PositionToCellCoord(points[i], smoothingRadius);
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

    
    public void ForeachPointWithinRadius(Vector2 centerPoint, Action<int> action)
    {
        (int centerX, int centerY) = PositionToCellCoord(centerPoint, smoothingRadius);
        float sqrRadius = smoothingRadius * smoothingRadius;
        int idx = 0;
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
                    action(particleIndex);
                }
            }
        }
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
