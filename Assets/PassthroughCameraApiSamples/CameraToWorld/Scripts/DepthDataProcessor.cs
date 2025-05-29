
using UnityEngine;
using System.IO;
using System.Collections.Generic;

namespace PassthroughCameraSamples.CameraToWorld
{
    public static class DepthDataProcessor
    {
        // Convert depth value to linear depth using z-buffer parameters
        public static float DepthToLinear(float depth, Vector4 zBufferParams)
        {
            // Based on EnvironmentDepthUtils
            float invDepthFactor = zBufferParams.x;
            float depthOffset = zBufferParams.y;

            // Convert from NDC depth to linear depth
            float ndcDepth = depth * 2.0f - 1.0f;
            return invDepthFactor / (ndcDepth + depthOffset);
        }

        // Reconstruct 3D point from depth pixel
        public static Vector3 DepthPixelToWorldPoint(int x, int y, float depth,
            int width, int height, Matrix4x4 reprojectionMatrix, Vector4 zBufferParams)
        {
            // Convert to normalized coordinates
            float u = (float)x / width;
            float v = (float)y / height;

            // Convert to NDC
            float ndcX = u * 2.0f - 1.0f;
            float ndcY = 1.0f - v * 2.0f; // Flip Y

            // Get linear depth
            float linearDepth = DepthToLinear(depth, zBufferParams);

            // Create point in NDC space
            Vector4 ndcPoint = new Vector4(ndcX, ndcY, depth, 1.0f);

            // Transform using inverse reprojection matrix
            Matrix4x4 invReprojection = reprojectionMatrix.inverse;
            Vector4 worldPoint = invReprojection * ndcPoint;

            // Perspective divide
            if (worldPoint.w != 0)
            {
                worldPoint /= worldPoint.w;
            }

            return new Vector3(worldPoint.x, worldPoint.y, worldPoint.z);
        }

        // Generate point cloud from saved depth data
        public static List<Vector3> LoadAndGeneratePointCloud(string depthMetaPath, string depthRawPath,
            int downsampleFactor = 4)
        {
            List<Vector3> points = new List<Vector3>();

            try
            {
                // Load metadata
                string jsonString = File.ReadAllText(depthMetaPath);
                var metadata = JsonUtility.FromJson<DepthMetadata>(jsonString);

                // Load raw depth values
                float[] depthValues;
                using (BinaryReader reader = new BinaryReader(File.Open(depthRawPath, FileMode.Open)))
                {
                    int pixelCount = metadata.width * metadata.height;
                    depthValues = new float[pixelCount];
                    for (int i = 0; i < pixelCount; i++)
                    {
                        depthValues[i] = reader.ReadSingle();
                    }
                }

                // Reconstruct reprojection matrix
                Matrix4x4 reprojectionMatrix = ArrayToMatrix(metadata.reprojectionMatrix);
                Vector4 zBufferParams = new Vector4(
                    metadata.zBufferParams.x,
                    metadata.zBufferParams.y,
                    metadata.zBufferParams.z,
                    metadata.zBufferParams.w
                );

                // Generate point cloud
                for (int y = 0; y < metadata.height; y += downsampleFactor)
                {
                    for (int x = 0; x < metadata.width; x += downsampleFactor)
                    {
                        int index = y * metadata.width + x;
                        float depth = depthValues[index];

                        if (depth > 0.01f && depth < 1.0f) // Valid depth range in NDC
                        {
                            Vector3 worldPoint = DepthPixelToWorldPoint(
                                x, y, depth,
                                metadata.width, metadata.height,
                                reprojectionMatrix, zBufferParams
                            );
                            points.Add(worldPoint);
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error loading depth data: {e.Message}");
            }

            return points;
        }

        private static Matrix4x4 ArrayToMatrix(float[] array)
        {
            Matrix4x4 matrix = new Matrix4x4();
            for (int i = 0; i < 16; i++)
            {
                matrix[i] = array[i];
            }
            return matrix;
        }
    }

    // Helper class for JSON deserialization
    [System.Serializable]
    public class DepthMetadata
    {
        public int width;
        public int height;
        public CameraTransform cameraPosition;
        public CameraRotation cameraRotation;
        public float[] reprojectionMatrix;
        public ZBufferParams zBufferParams;
        public float minDepth;
        public float maxDepth;
    }

    [System.Serializable]
    public class CameraTransform
    {
        public float x, y, z;
    }

    [System.Serializable]
    public class CameraRotation
    {
        public float x, y, z, w;
    }

    [System.Serializable]
    public class ZBufferParams
    {
        public float x, y, z, w;
    }
}