using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using UnityEngine.U2D;
using Unity.VisualScripting;


[RequireComponent(typeof(MeshFilter)), RequireComponent(typeof(MeshRenderer)), RequireComponent(typeof(EdgeCollider2D)), RequireComponent(typeof(WaterTriggerHandler))]
public class InteractableWater : MonoBehaviour
{
    [Header("Mesh Generation")]
    [Range(2, 500)] public int NumOfXVertices = 50;
    public float Width = 10f;
    public float Height = 4f;
    public Material WaterMaterial;
    private const int NUM_OF_Y_VERTICES = 2;

    [Header("Gizmo")]
    public Color GizmoColor = Color.white;

    private Mesh _mesh;
    private MeshRenderer _meshRenderer;
    private MeshFilter _meshFilter;
    private Vector3[] _vertices;
    private int[] _topVerticesIndex;

    private EdgeCollider2D _coll;

    private void Start()
    {
        GenerateMesh();
    }

    private void Reset()
    {
        _coll = GetComponent<EdgeCollider2D>();
        _coll.isTrigger = true;
    }

    public void ResetEdgeCollider()
    {
        _coll = GetComponent<EdgeCollider2D>();

        Vector2[] newPoints = new Vector2[2];

        Vector2 firstPoint = new Vector2(_vertices[_topVerticesIndex[0]].x, _vertices[_topVerticesIndex[0]].y);
        newPoints[0] = firstPoint;

        Vector2 secondPoint = new Vector2(_vertices[_topVerticesIndex[NumOfXVertices - 1]].x, _vertices[_topVerticesIndex[NumOfXVertices - 1]].y);
        newPoints[1] = secondPoint;

        _coll.offset = Vector2.zero;
        _coll.points = newPoints;
    }

    public void GenerateMesh()
    {
        _mesh = new Mesh();

        _vertices = new Vector3[NumOfXVertices * NUM_OF_Y_VERTICES];
        _topVerticesIndex = new int[NumOfXVertices];
        for (int y = 0; y < NUM_OF_Y_VERTICES; y++)
        {
            for (int x = 0; x < NumOfXVertices; x++)
            {
                int index = y * NumOfXVertices + x;
                float xPos = (x / (float)(NumOfXVertices - 1)) * Width - Width / 2f;
                float yPos = (y / (float)(NUM_OF_Y_VERTICES - 1)) * Height - Height / 2f;
                _vertices[index] = new UnityEngine.Vector3(xPos, yPos, 0f);

                if (y == NUM_OF_Y_VERTICES - 1)
                    _topVerticesIndex[x] = index;
            }
        }

        int[] triangles = new int[(NumOfXVertices - 1) * (NUM_OF_Y_VERTICES - 1) * 6];
        int t = 0;

        for (int y = 0; y < NUM_OF_Y_VERTICES - 1; y++)
        {
            for (int x = 0; x < NumOfXVertices - 1; x++)
            {
                int i = y * NumOfXVertices + x;

                int bottomLeft = y * NumOfXVertices + x;
                int bottomRight = bottomLeft + 1;
                int topLeft = bottomLeft + NumOfXVertices;
                int topRight = topLeft + 1;

                triangles[t++] = bottomLeft;
                triangles[t++] = bottomRight;
                triangles[t++] = topLeft;

                triangles[t++] = bottomRight;
                triangles[t++] = topRight;
                triangles[t++] = topLeft;
            }
        }

        Vector2[] uvs = new Vector2[_vertices.Length];
        for (int i = 0; i < _vertices.Length; i++)
        {
            uvs[i] = new Vector2(_vertices[i].x + Width / 2, (_vertices[i].y + Height / 2) / Height);
        }

        if (_meshRenderer == null)
            _meshRenderer = GetComponent<MeshRenderer>();

        if (_meshFilter == null)
            _meshFilter = GetComponent<MeshFilter>();

        _meshRenderer.material = WaterMaterial;

        _mesh.vertices = _vertices;
        _mesh.triangles = triangles;
        _mesh.uv = uvs;

        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();

        _meshFilter.mesh = _mesh;
    }
}

[CustomEditor(typeof(InteractableWater))]
public class InteractableWaterEditor : Editor
{
    private InteractableWater _water;

    private void OnEnable()
    {
        _water = (InteractableWater)target;
    }

    public override VisualElement CreateInspectorGUI()
    {
        VisualElement root = new VisualElement();

        InspectorElement.FillDefaultInspector(root, serializedObject, this);

        root.Add(new VisualElement { style = { height = 10 } });

        Button generateMeshButtom = new Button(() => _water.GenerateMesh())
        {
            text = "Generate Mesh"
        };
        root.Add(generateMeshButtom);

        Button placeEdgeColliderButton = new Button(() => _water.ResetEdgeCollider())
        {
            text = "Reset Edge Collider"
        };

        return root;
    }

    private void ChangeDimensions(ref float width, ref float height, float calculateWidthMax, float calculateHeightMax)
    {
        width = Mathf.Max(0.1f, calculateWidthMax);
        height = Mathf.Max(0.1f, calculateHeightMax);
    }

    private void OnSceneGUI()
    {
        Handles.color = _water.GizmoColor;
        Vector3 center = _water.transform.position;
        Vector3 size = new Vector3(_water.Width, _water.Height, 0.1f);
        Handles.DrawWireCube(center, size);

        float handleSize = HandleUtility.GetHandleSize(center) * 0.1f;
        Vector3 snap = Vector3.one;

        Vector3[] corners = new Vector3[4];
        corners[0] = center + new Vector3(-_water.Width / 2, -_water.Height / 2, 0); // Bottom Left
        corners[1] = center + new Vector3(-_water.Width / 2, _water.Height / 2, 0);  // Top Left
        corners[2] = center + new Vector3(_water.Width / 2, _water.Height / 2, 0);   // Top Right
        corners[3] = center + new Vector3(_water.Width / 2, -_water.Height / 2, 0);  // Bottom Right

        // BOTTOM LEFT
        EditorGUI.BeginChangeCheck();
        Vector3 newBL = Handles.FreeMoveHandle(corners[0], handleSize, snap, Handles.CubeHandleCap);
        if (EditorGUI.EndChangeCheck())
        {
            float newWidth = corners[3].x - newBL.x;
            float newHeight = corners[1].y - newBL.y;

            _water.Width = Mathf.Max(0.1f, newWidth);
            _water.Height = Mathf.Max(0.1f, newHeight);

            // Move center accordingly
            Vector3 delta = newBL - corners[0];
            _water.transform.position += delta * 0.5f;
        }

        // BOTTOM RIGHT
        EditorGUI.BeginChangeCheck();
        Vector3 newBR = Handles.FreeMoveHandle(corners[3], handleSize, snap, Handles.CubeHandleCap);
        if (EditorGUI.EndChangeCheck())
        {
            float newWidth = newBR.x - corners[0].x;
            float newHeight = corners[1].y - newBR.y;

            _water.Width = Mathf.Max(0.1f, newWidth);
            _water.Height = Mathf.Max(0.1f, newHeight);

            Vector3 delta = newBR - corners[3];
            _water.transform.position += delta * 0.5f;
        }

        EditorGUI.BeginChangeCheck();
        Vector3 newTL = Handles.FreeMoveHandle(corners[1], handleSize, snap, Handles.CubeHandleCap);
        if (EditorGUI.EndChangeCheck())
        {
            float newWidth = corners[3].x - newTL.x;
            float newHeight = newTL.y - corners[0].y;

            _water.Width = Mathf.Max(0.1f, newWidth);
            _water.Height = Mathf.Max(0.1f, newHeight);

            Vector3 delta = newTL - corners[1];
            _water.transform.position += delta * 0.5f;
        }

        // TOP RIGHT
        EditorGUI.BeginChangeCheck();
        Vector3 newTR = Handles.FreeMoveHandle(corners[2], handleSize, snap, Handles.CubeHandleCap);
        if (EditorGUI.EndChangeCheck())
        {
            float newWidth = newTR.x - corners[0].x;
            float newHeight = newTR.y - corners[0].y;

            _water.Width = Mathf.Max(0.1f, newWidth);
            _water.Height = Mathf.Max(0.1f, newHeight);

            Vector3 delta = newTR - corners[2];
            _water.transform.position += delta * 0.5f;
        }

        if (GUI.changed)
        {
            _water.GenerateMesh();
        }
    }
}