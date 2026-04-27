using UnityEngine;

public class RDSController : MonoBehaviour
{
    // =========================================================
    // Quad
    // =========================================================

    [Header("Quad Renderers")]
    public Renderer leftQuadRenderer;
    public Renderer rightQuadRenderer;

    // =========================================================
    // Image / Display
    // =========================================================

    [Header("Resolution")]
    public int widthPx = 3840;
    public int heightPx = 2160;

    [Header("Display")]
    [Tooltip("pixel pitch [mm/px]")]
    public float pp = 0.1845236f;

    [Tooltip("1 world unit が何 mm か")]
    public float worldUnitToMm = 1.0f;

    // =========================================================
    // Viewer
    // =========================================================

    [Header("Viewer Position [world]")]
    [Tooltip("ディスプレイ中心基準．右が+")]
    public float viewerXWorld = 0.0f;

    [Tooltip("ディスプレイ中心基準．上が+")]
    public float viewerYWorld = 0.0f;

    [Tooltip("ディスプレイ面から観察者までの距離")]
    public float viewerZWorld = 1000.0f;

    [Header("IPD")]
    public float IPD = 63.0f;

    // =========================================================
    // Target disparity angle
    // =========================================================

    [Header("Target Disparity Angle [deg]")]
    [Tooltip("alpha - beta の目標値．正なら正方向，負なら負方向")]
    public float targetAngleDeg = 0.5f;

    // =========================================================
    // Circle stimulus in display-world coordinates
    // =========================================================

    [Header("Circle Stimulus [world]")]
    [Tooltip("ディスプレイ中心基準．右が+")]
    public float circleCenterXWorld = 0.0f;

    [Tooltip("ディスプレイ中心基準．上が+")]
    public float circleCenterYWorld = 0.0f;

    [Tooltip("円の半径．worldUnitToMmでmmに変換される")]
    public float circleRadiusWorld = 30.0f;

    // =========================================================
    // Disparity search
    // =========================================================

    [Header("Disparity Search Range [px]")]
    public int positiveDispMinPx = 1;
    public int positiveDispMaxPx = 200;
    public int negativeDispMinPx = -200;
    public int negativeDispMaxPx = -1;

    // =========================================================
    // Random dot
    // =========================================================

    [Header("Random Dot")]
    [Range(0.0f, 1.0f)]
    public float pWhite = 0.5f;

    public float backgroundSeed = 0.0f;
    public float objectSeed = 100.0f;

    // =========================================================
    // Debug / Update
    // =========================================================

    [Header("Debug / Update")]
    public bool showCircleGuide = false;
    public bool autoUpdate = true;
    public bool autoSetQuadAspect = false;

    // =========================================================
    // Result
    // =========================================================

    [Header("Result")]
    [SerializeField] private int bestDisparityPx;
    [SerializeField] private float halfDisparityPx;
    [SerializeField] private float actualAngleDeg;
    [SerializeField] private float errorDeg;

    [SerializeField] private float leftShiftPx;
    [SerializeField] private float rightShiftPx;

    [SerializeField] private float circleCenterXPx;
    [SerializeField] private float circleCenterYPx;
    [SerializeField] private float circleRadiusPx;

    [SerializeField] private float viewerXmm;
    [SerializeField] private float viewerYmm;
    [SerializeField] private float viewerZmm;

    [SerializeField] private float circleCenterXmm;
    [SerializeField] private float circleCenterYmm;
    [SerializeField] private float circleRadiusMm;

    private Material leftMat;
    private Material rightMat;

    // =========================================================
    // Unity events
    // =========================================================

    void Start()
    {
        InitializeMaterials();
        UpdateRDS();
    }

    void Update()
    {
        if (autoUpdate)
        {
            UpdateRDS();
        }
    }

    void OnValidate()
    {
        widthPx = Mathf.Max(1, widthPx);
        heightPx = Mathf.Max(1, heightPx);
        pp = Mathf.Max(0.000001f, pp);
        worldUnitToMm = Mathf.Max(0.000001f, worldUnitToMm);
        IPD = Mathf.Max(0.0f, IPD);
        circleRadiusWorld = Mathf.Max(0.0f, circleRadiusWorld);

        if (positiveDispMinPx > positiveDispMaxPx)
        {
            positiveDispMinPx = positiveDispMaxPx;
        }

        if (negativeDispMinPx > negativeDispMaxPx)
        {
            negativeDispMinPx = negativeDispMaxPx;
        }
    }

    // =========================================================
    // Initialization
    // =========================================================

    void InitializeMaterials()
    {
        if (leftQuadRenderer == null || rightQuadRenderer == null)
        {
            Debug.LogError("LeftQuadRenderer または RightQuadRenderer が未設定です。");
            return;
        }

        // 左右Quadに別々のMaterialを貼っておく前提
        // RDS_L.mat: _EyeSign = +1
        // RDS_R.mat: _EyeSign = -1
        leftMat = leftQuadRenderer.material;
        rightMat = rightQuadRenderer.material;

        if (leftQuadRenderer.sharedMaterial == rightQuadRenderer.sharedMaterial)
        {
            Debug.LogWarning(
                "LeftQuad と RightQuad が同じMaterialアセットを参照しています。" +
                "RDS_L.mat と RDS_R.mat を別々に用意して割り当ててください。"
            );
        }

        if (leftMat == null || rightMat == null)
        {
            Debug.LogError("左右いずれかのMaterialがnullです。");
            return;
        }

        if (leftMat.shader == null || leftMat.shader.name != "Unlit/RDS")
        {
            Debug.LogWarning($"Left material shader is {leftMat.shader?.name}. Unlit/RDS を指定してください。");
        }

        if (rightMat.shader == null || rightMat.shader.name != "Unlit/RDS")
        {
            Debug.LogWarning($"Right material shader is {rightMat.shader?.name}. Unlit/RDS を指定してください。");
        }
    }

    // =========================================================
    // Main update
    // =========================================================

    public void UpdateRDS()
    {
        if (leftMat == null || rightMat == null)
        {
            InitializeMaterials();
        }

        if (leftMat == null || rightMat == null)
        {
            return;
        }

        UpdateDerivedParameters();

        bestDisparityPx = SolveDisparityForTargetAngle();
        halfDisparityPx = bestDisparityPx / 2.0f;

        actualAngleDeg = DispPxToAngleDeg(bestDisparityPx);
        errorDeg = Mathf.Abs(actualAngleDeg - targetAngleDeg);

        // デバッグ表示用
        // 実際の描画ではShader側で _EyeSign * _HalfDisparityPx する
        leftShiftPx = halfDisparityPx;
        rightShiftPx = -halfDisparityPx;

        ApplyMaterialParams(leftMat);
        ApplyMaterialParams(rightMat);

        if (autoSetQuadAspect)
        {
            ApplyQuadAspect();
        }
    }

    // =========================================================
    // Derived parameters
    // =========================================================

    void UpdateDerivedParameters()
    {
        // world -> mm
        viewerXmm = viewerXWorld * worldUnitToMm;
        viewerYmm = viewerYWorld * worldUnitToMm;
        viewerZmm = viewerZWorld * worldUnitToMm;

        circleCenterXmm = circleCenterXWorld * worldUnitToMm;
        circleCenterYmm = circleCenterYWorld * worldUnitToMm;
        circleRadiusMm = circleRadiusWorld * worldUnitToMm;

        // mm -> px
        // world座標:
        //   x: 右が+
        //   y: 上が+
        //
        // shader内pixel座標:
        //   x: 右が+
        //   y: 下が+
        circleCenterXPx = (widthPx / 2.0f) + (circleCenterXmm / pp);
        circleCenterYPx = (heightPx / 2.0f) - (circleCenterYmm / pp);
        circleRadiusPx = circleRadiusMm / pp;
    }

    // =========================================================
    // Material
    // =========================================================

    void ApplyMaterialParams(Material mat)
    {
        // EyeSignはMaterial側で設定する
        // それ以外の数値はControllerから渡す

        mat.SetFloat("_WidthPx", widthPx);
        mat.SetFloat("_HeightPx", heightPx);

        mat.SetFloat("_CircleCenterXPx", circleCenterXPx);
        mat.SetFloat("_CircleCenterYPx", circleCenterYPx);
        mat.SetFloat("_CircleRadiusPx", circleRadiusPx);

        mat.SetFloat("_HalfDisparityPx", halfDisparityPx);

        mat.SetFloat("_PWhite", pWhite);
        mat.SetFloat("_BackgroundSeed", backgroundSeed);
        mat.SetFloat("_ObjectSeed", objectSeed);

        mat.SetFloat("_ShowCircleGuide", showCircleGuide ? 1.0f : 0.0f);
    }

    // =========================================================
    // Disparity angle calculation
    // =========================================================

    float DispPxToAngleDeg(float dispPx)
    {
        float shiftMm = dispPx * pp;

        // 融合後の見かけ中心
        float mx = circleCenterXmm;
        float my = circleCenterYmm;

        // 左右画像上の対応点
        // 正のdisp:
        //   left  = +disp/2
        //   right = -disp/2
        float lx = mx + shiftMm / 2.0f;
        float rx = mx - shiftMm / 2.0f;

        Vector3 leftEye = new Vector3(
            viewerXmm - IPD / 2.0f,
            viewerYmm,
            viewerZmm
        );

        Vector3 rightEye = new Vector3(
            viewerXmm + IPD / 2.0f,
            viewerYmm,
            viewerZmm
        );

        Vector3 leftPoint = new Vector3(lx, my, 0.0f);
        Vector3 rightPoint = new Vector3(rx, my, 0.0f);
        Vector3 midPoint = new Vector3(mx, my, 0.0f);

        Vector3 l1 = leftPoint - leftEye;
        Vector3 r1 = rightPoint - rightEye;

        Vector3 l2 = midPoint - leftEye;
        Vector3 r2 = midPoint - rightEye;

        float alpha = Vector3.Angle(l1, r1);
        float beta = Vector3.Angle(l2, r2);

        return alpha - beta;
    }

    // =========================================================
    // Search
    // =========================================================

    int SolveDisparityForTargetAngle()
    {
        if (Mathf.Approximately(targetAngleDeg, 0.0f))
        {
            return 0;
        }

        int start;
        int end;

        if (targetAngleDeg > 0.0f)
        {
            start = positiveDispMinPx;
            end = positiveDispMaxPx;
        }
        else
        {
            start = negativeDispMinPx;
            end = negativeDispMaxPx;
        }

        if (start > end)
        {
            Debug.LogError("視差探索範囲が不正です。min <= max になるように設定してください。");
            return 0;
        }

        int bestDisp = start;
        float bestTheta = DispPxToAngleDeg(start);
        float bestError = Mathf.Abs(bestTheta - targetAngleDeg);

        for (int d = start; d <= end; d++)
        {
            float theta = DispPxToAngleDeg(d);
            float err = Mathf.Abs(theta - targetAngleDeg);

            if (err < bestError)
            {
                bestDisp = d;
                bestTheta = theta;
                bestError = err;
            }
        }

        return bestDisp;
    }

    // =========================================================
    // Quad aspect
    // =========================================================

    void ApplyQuadAspect()
    {
        float aspect = (float)widthPx / heightPx;

        ApplyAspectToRenderer(leftQuadRenderer, aspect);
        ApplyAspectToRenderer(rightQuadRenderer, aspect);
    }

    void ApplyAspectToRenderer(Renderer renderer, float aspect)
    {
        if (renderer == null)
        {
            return;
        }

        Transform t = renderer.transform;
        Vector3 s = t.localScale;

        // yを基準にxだけ調整
        t.localScale = new Vector3(s.y * aspect, s.y, s.z);
    }

}