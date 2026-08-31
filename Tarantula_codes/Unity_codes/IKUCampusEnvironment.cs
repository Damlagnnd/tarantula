using UnityEngine;
using TMPro;

[ExecuteAlways]
public class IKUCampusEnvironment : MonoBehaviour
{
    [Header("Genel Sahne")]
    public float scale = 1f;

    [Header("Oluşturma Ayarları")]
    [Tooltip("Obje boşsa sahneyi otomatik bir kere oluşturur. Daha sonra elle yaptığın değişiklikleri bozmaz.")]
    public bool generateIfEmpty = true;

    [Header("TMP Font")]
    [Tooltip("Buraya bir TMP Font Asset ata. Örn: LiberationSans SDF")]
    public TMP_FontAsset tmpFont;

    [Header("Bina Renkleri")]
    public Color buildingColor = new Color(0.62f, 0.55f, 0.43f);
    public Color glassColor = new Color(0.05f, 0.25f, 0.45f, 0.65f);
    public Color darkMetalColor = new Color(0.08f, 0.08f, 0.08f);
    public Color redColor = new Color(0.75f, 0.02f, 0.02f);
    public Color floorColor = new Color(0.35f, 0.34f, 0.32f);
    public Color treeColor = new Color(0.18f, 0.32f, 0.18f);
    public Color whiteColor = Color.white;

    Material buildingMat;
    Material glassMat;
    Material darkMat;
    Material redMat;
    Material floorMat;
    Material windowMat;
    Material treeMat;
    Material whiteMat;

    void OnEnable()
    {
        
        if (generateIfEmpty && transform.childCount == 0)
        {
            BuildScene();
        }
    }

    [ContextMenu("Regenerate Campus")]
    public void RegenerateCampus()
    {
        BuildScene();
    }

    [ContextMenu("Clear Campus")]
    public void ClearCampus()
    {
        ClearChildren();
    }

    public void BuildScene()
    {
        if (transform == null) return;

        ClearChildren();
        CreateMaterials();

        CreateGround();
        CreateMainBuilding();
        CreateCentralGlassEntrance();
        CreateWindows();
        CreateEntranceStairs();
        CreateCanopy();
        CreateColumns();
        CreateRedPlanters();
        CreateSchoolTexts();
        CreateTurkishFlag();
        CreateSideDecorations();
        CreateLighting();
    }

    void ClearChildren()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);

            if (Application.isPlaying)
                Destroy(child.gameObject);
            else
                DestroyImmediate(child.gameObject);
        }
    }

    void CreateMaterials()
    {
        buildingMat = CreateMat("Building Beige", buildingColor, 0.18f);
        glassMat = CreateTransparentMat("Blue Glass", glassColor);
        darkMat = CreateMat("Dark Metal", darkMetalColor, 0.08f);
        redMat = CreateMat("IKU Red", redColor, 0.15f);
        floorMat = CreateMat("Stone Floor", floorColor, 0.22f);
        windowMat = CreateTransparentMat("Window Glass", new Color(0.08f, 0.28f, 0.48f, 0.75f));
        treeMat = CreateMat("Tree Green", treeColor, 0.05f);
        whiteMat = CreateMat("Flag White", whiteColor, 0.2f);
    }

    Material CreateMat(string name, Color color, float glossiness = 0.25f)
    {
        Material mat = new Material(Shader.Find("Standard"));
        mat.name = name;
        mat.color = color;
        mat.SetFloat("_Glossiness", glossiness);
        return mat;
    }

    Material CreateTransparentMat(string name, Color color)
    {
        Material mat = new Material(Shader.Find("Standard"));
        mat.name = name;
        mat.color = color;
        mat.SetFloat("_Glossiness", 0.75f);
        mat.SetFloat("_Metallic", 0.05f);

        mat.SetFloat("_Mode", 3);
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = 3000;

        return mat;
    }

    GameObject Cube(string name, Vector3 position, Vector3 scaleValue, Material mat)
    {
        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obj.name = name;
        obj.transform.SetParent(transform);
        obj.transform.localPosition = position * scale;
        obj.transform.localScale = scaleValue * scale;

        Renderer r = obj.GetComponent<Renderer>();
        if (r != null && mat != null)
            r.sharedMaterial = mat;

        return obj;
    }

    void CreateGround()
    {
        Cube(
            "Large Stone Ground",
            new Vector3(0, -0.05f, -8),
            new Vector3(60, 0.1f, 45),
            floorMat
        );

        for (int x = -28; x <= 28; x += 4)
        {
            Cube(
                "Ground Tile Line X",
                new Vector3(x, 0.01f, -8),
                new Vector3(0.05f, 0.03f, 45),
                darkMat
            );
        }

        for (int z = -28; z <= 10; z += 4)
        {
            Cube(
                "Ground Tile Line Z",
                new Vector3(0, 0.02f, z),
                new Vector3(60, 0.03f, 0.05f),
                darkMat
            );
        }
    }

    void CreateMainBuilding()
    {
        GameObject left = Cube(
            "Left University Building",
            new Vector3(-17, 8, 10),
            new Vector3(24, 16, 2.2f),
            buildingMat
        );
        left.transform.localRotation = Quaternion.Euler(0, -8f, 0);

        GameObject right = Cube(
            "Right University Building",
            new Vector3(17, 8, 10),
            new Vector3(24, 16, 2.2f),
            buildingMat
        );
        right.transform.localRotation = Quaternion.Euler(0, 8f, 0);

        Cube(
            "Middle Back Building",
            new Vector3(0, 7, 12),
            new Vector3(10, 14, 2),
            buildingMat
        );

        Cube(
            "Dark Entrance Base Left",
            new Vector3(-17, 2.2f, 8.2f),
            new Vector3(25, 3, 2.5f),
            darkMat
        );

        Cube(
            "Dark Entrance Base Right",
            new Vector3(17, 2.2f, 8.2f),
            new Vector3(25, 3, 2.5f),
            darkMat
        );
    }

    void CreateCentralGlassEntrance()
    {
        Cube(
            "Central Blue Glass Front",
            new Vector3(0, 7.3f, 8),
            new Vector3(8, 10, 0.35f),
            glassMat
        );

        Cube(
            "Central Dark Door",
            new Vector3(0, 1.8f, 7.7f),
            new Vector3(4, 3.2f, 0.4f),
            darkMat
        );

        for (int x = -3; x <= 3; x += 2)
        {
            Cube(
                "Glass Vertical Frame",
                new Vector3(x, 7.3f, 7.45f),
                new Vector3(0.08f, 10, 0.1f),
                darkMat
            );
        }

        for (int y = 3; y <= 12; y += 2)
        {
            Cube(
                "Glass Horizontal Frame",
                new Vector3(0, y, 7.43f),
                new Vector3(8, 0.08f, 0.1f),
                darkMat
            );
        }

        Cube(
            "Orange Entrance Frame Top",
            new Vector3(0, 3.7f, 7.3f),
            new Vector3(5, 0.35f, 0.35f),
            redMat
        );

        Cube(
            "Orange Entrance Frame Left",
            new Vector3(-2.5f, 2.1f, 7.3f),
            new Vector3(0.35f, 3.2f, 0.35f),
            redMat
        );

        Cube(
            "Orange Entrance Frame Right",
            new Vector3(2.5f, 2.1f, 7.3f),
            new Vector3(0.35f, 3.2f, 0.35f),
            redMat
        );
    }

    void CreateWindows()
    {
        CreateWindowGrid(-17, 10.1f, -8f);
        CreateWindowGrid(17, 10.1f, 8f);
    }

    void CreateWindowGrid(float centerX, float z, float rotationY)
    {
        for (int row = 0; row < 5; row++)
        {
            for (int col = -4; col <= 4; col += 2)
            {
                float x = centerX + col * 2.2f;
                float y = 4f + row * 2.4f;

                GameObject frame = Cube(
                    "Window Frame",
                    new Vector3(x, y, z - 1.27f),
                    new Vector3(2.35f, 1.25f, 0.05f),
                    darkMat
                );
                frame.transform.localRotation = Quaternion.Euler(0, rotationY, 0);

                GameObject w = Cube(
                    "Building Window",
                    new Vector3(x, y, z - 1.32f),
                    new Vector3(2.1f, 1.0f, 0.06f),
                    windowMat
                );
                w.transform.localRotation = Quaternion.Euler(0, rotationY, 0);
            }
        }
    }

    void CreateEntranceStairs()
    {
        for (int i = 0; i < 5; i++)
        {
            Cube(
                "Entrance Step",
                new Vector3(0, 0.12f + i * 0.18f, 2.2f + i * 0.55f),
                new Vector3(12 - i * 1.4f, 0.22f, 1.1f),
                darkMat
            );
        }

        Cube(
            "Entrance Platform",
            new Vector3(0, 1.05f, 5.3f),
            new Vector3(9, 0.25f, 3),
            darkMat
        );
    }

    void CreateCanopy()
    {
        Cube(
            "Long Black Entrance Canopy",
            new Vector3(0, 4.0f, 5.4f),
            new Vector3(44, 0.45f, 3.2f),
            darkMat
        );

        Cube(
            "Canopy Front Lip",
            new Vector3(0, 3.65f, 3.8f),
            new Vector3(44, 0.35f, 0.35f),
            darkMat
        );
    }

    void CreateColumns()
    {
        for (int x = -22; x <= 22; x += 6)
        {
            GameObject col = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            col.name = "Black Entrance Column";
            col.transform.SetParent(transform);
            col.transform.localPosition = new Vector3(x, 2f, 4.4f) * scale;
            col.transform.localScale = new Vector3(0.25f, 2f, 0.25f) * scale;

            Renderer r = col.GetComponent<Renderer>();
            if (r != null)
                r.sharedMaterial = darkMat;
        }
    }

    void CreateRedPlanters()
    {
        for (int x = -18; x <= 18; x += 9)
        {
            Cube(
                "Red Planter",
                new Vector3(x, 0.45f, 1.2f),
                new Vector3(2.2f, 0.9f, 1.2f),
                redMat
            );

            GameObject tree = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            tree.name = "Small Tree Trunk";
            tree.transform.SetParent(transform);
            tree.transform.localPosition = new Vector3(x, 1.4f, 1.2f) * scale;
            tree.transform.localScale = new Vector3(0.12f, 1.2f, 0.12f) * scale;
            tree.GetComponent<Renderer>().sharedMaterial = darkMat;

            GameObject crown = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            crown.name = "Small Tree Crown";
            crown.transform.SetParent(transform);
            crown.transform.localPosition = new Vector3(x, 2.7f, 1.2f) * scale;
            crown.transform.localScale = new Vector3(1.2f, 1.2f, 1.2f) * scale;
            crown.GetComponent<Renderer>().sharedMaterial = treeMat;
        }
    }

    void CreateSchoolTexts()
    {
        CreateText3D(
            "T.C.",
            new Vector3(-17, 18.65f, 8.25f),
            new Vector3(0, -8f, 0),
            1.05f,
            redColor,
            30f
        );

        CreateText3D(
            "İSTANBUL",
            new Vector3(-17, 17.55f, 8.25f),
            new Vector3(0, -8f, 0),
            1.05f,
            redColor,
            30f
        );

        CreateText3D(
            "KÜLTÜR ÜNİVERSİTESİ",
            new Vector3(-17, 16.25f, 8.25f),
            new Vector3(0, -8f, 0),
            1.25f,
            redColor,
            34f
        );

        CreateText3D(
            "T.C.",
            new Vector3(17, 18.65f, 8.25f),
            new Vector3(0, 8f, 0),
            1.05f,
            redColor,
            30f
        );

        CreateText3D(
            "İSTANBUL",
            new Vector3(17, 17.55f, 8.25f),
            new Vector3(0, 8f, 0),
            1.05f,
            redColor,
            30f
        );

        CreateText3D(
            "KÜLTÜR ÜNİVERSİTESİ",
            new Vector3(17, 16.25f, 8.25f),
            new Vector3(0, 8f, 0),
            1.25f,
            redColor,
            34f
        );
    }

    void CreateText3D(string text, Vector3 position, Vector3 rotation, float size, Color color, float width = 28f)
    {
        GameObject textObj = new GameObject("Text - " + text);
        textObj.transform.SetParent(transform);
        textObj.transform.localPosition = position * scale;
        textObj.transform.localEulerAngles = rotation;
        textObj.transform.localScale = Vector3.one * size * scale;

        TextMeshPro tmp = textObj.AddComponent<TextMeshPro>();

        if (tmpFont != null)
            tmp.font = tmpFont;
        else if (TMP_Settings.defaultFontAsset != null)
            tmp.font = TMP_Settings.defaultFontAsset;

        tmp.text = text;
        tmp.fontSize = 7.5f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = color;
        tmp.fontStyle = FontStyles.Bold;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.horizontalAlignment = HorizontalAlignmentOptions.Center;
        tmp.verticalAlignment = VerticalAlignmentOptions.Middle;

        RectTransform rect = tmp.rectTransform;
        rect.sizeDelta = new Vector2(width, 8f);
    }

    void CreateTurkishFlag()
    {
        GameObject flagBase = Cube(
            "Turkish Flag Red Panel",
            new Vector3(0, 15.7f, 7.05f),
            new Vector3(4.6f, 2.9f, 0.12f),
            redMat
        );

        flagBase.transform.localRotation = Quaternion.Euler(0, 0, 0);

        GameObject crescentOuter = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        crescentOuter.name = "Turkish Flag Crescent Outer";
        crescentOuter.transform.SetParent(transform);
        crescentOuter.transform.localPosition = new Vector3(-0.85f, 15.7f, 6.93f) * scale;
        crescentOuter.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        crescentOuter.transform.localScale = new Vector3(0.62f, 0.035f, 0.62f) * scale;
        crescentOuter.GetComponent<Renderer>().sharedMaterial = whiteMat;

        GameObject crescentInner = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        crescentInner.name = "Turkish Flag Crescent Inner Red Cut";
        crescentInner.transform.SetParent(transform);
        crescentInner.transform.localPosition = new Vector3(-0.60f, 15.7f, 6.88f) * scale;
        crescentInner.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        crescentInner.transform.localScale = new Vector3(0.50f, 0.04f, 0.50f) * scale;
        crescentInner.GetComponent<Renderer>().sharedMaterial = redMat;

        GameObject star = CreateStar(
            "Turkish Flag Star",
            new Vector3(0.65f, 15.7f, 6.87f),
            0.42f,
            whiteMat
        );

        star.transform.localRotation = Quaternion.Euler(0f, 0f, -18f);
    }

    GameObject CreateStar(string name, Vector3 position, float radius, Material mat)
    {
        GameObject star = new GameObject(name);
        star.transform.SetParent(transform);
        star.transform.localPosition = position * scale;
        star.transform.localScale = Vector3.one * scale;

        MeshFilter mf = star.AddComponent<MeshFilter>();
        MeshRenderer mr = star.AddComponent<MeshRenderer>();

        Mesh mesh = new Mesh();

        Vector3[] vertices = new Vector3[11];
        int[] triangles = new int[30];

        vertices[0] = Vector3.zero;

        float outer = radius;
        float inner = radius * 0.42f;

        for (int i = 0; i < 10; i++)
        {
            float angle = Mathf.Deg2Rad * (90f + i * 36f);
            float r = (i % 2 == 0) ? outer : inner;

            float x = Mathf.Cos(angle) * r;
            float y = Mathf.Sin(angle) * r;

            vertices[i + 1] = new Vector3(x, y, 0f);
        }

        for (int i = 0; i < 10; i++)
        {
            triangles[i * 3] = 0;
            triangles[i * 3 + 1] = i + 1;
            triangles[i * 3 + 2] = (i == 9) ? 1 : i + 2;
        }

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();

        mf.sharedMesh = mesh;

        if (mat != null)
            mr.sharedMaterial = mat;

        return star;
    }

    void CreateSideDecorations()
    {
        for (int x = -6; x <= 6; x += 3)
        {
            GameObject bollard = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            bollard.name = "Red Bollard";
            bollard.transform.SetParent(transform);
            bollard.transform.localPosition = new Vector3(x, 0.55f, -2.5f) * scale;
            bollard.transform.localScale = new Vector3(0.18f, 0.55f, 0.18f) * scale;
            bollard.GetComponent<Renderer>().sharedMaterial = redMat;
        }

        Cube(
            "Cafe Dark Storefront Left",
            new Vector3(-11, 1.6f, 3.7f),
            new Vector3(6, 2.4f, 0.3f),
            glassMat
        );

        Cube(
            "Cafe Dark Storefront Right",
            new Vector3(11, 1.6f, 3.7f),
            new Vector3(6, 2.4f, 0.3f),
            glassMat
        );
    }

    void CreateLighting()
    {
        GameObject sun = new GameObject("Sun Light");
        sun.transform.SetParent(transform);
        sun.transform.localRotation = Quaternion.Euler(45f, -25f, 0f);

        Light dir = sun.AddComponent<Light>();
        dir.type = LightType.Directional;
        dir.intensity = 1.3f;

        GameObject fill = new GameObject("Soft Fill Light");
        fill.transform.SetParent(transform);
        fill.transform.localPosition = new Vector3(0, 8, -10) * scale;

        Light fillLight = fill.AddComponent<Light>();
        fillLight.type = LightType.Point;
        fillLight.intensity = 2f;
        fillLight.range = 35f;

        RenderSettings.ambientLight = new Color(0.55f, 0.58f, 0.62f);
    }
}