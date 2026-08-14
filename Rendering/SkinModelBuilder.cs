using System.Numerics;

namespace GrinLauncher.Rendering;

// 마인크래프트 스킨 큐브 UV 매핑 규칙(이른바 "Box UV"). Mojang 플레이어 모델과
// 동일한 배치를 따른다 — 예: 머리(0,0)-(32,16) 블록에서 정면(얼굴)이 (8,8)에
// 오는 것 등은 바닐라 스킨 포맷 문서/에디터에서 공통으로 쓰는 값이다.
public static class SkinModelBuilder
{
    public const int VertexFloats = 5; // pos.xyz + uv.xy

    public readonly record struct BoxSpec(Vector3 Origin, Vector3 Size, Vector2 Uv, float Inflate = 0f, bool MirrorX = false);

    public static void AddBox(List<float> verts, List<uint> indices, BoxSpec spec, float texW, float texH)
    {
        var (ox, oy, oz) = (spec.Origin.X, spec.Origin.Y, spec.Origin.Z);
        var (w, h, d) = (spec.Size.X, spec.Size.Y, spec.Size.Z);
        var inf = spec.Inflate;

        // 박스를 inflate 만큼 부풀려서 min/max 코너 계산 (오버레이 레이어용)
        float x0 = ox - inf, x1 = ox + w + inf;
        float y0 = oy - inf, y1 = oy + h + inf;
        float z0 = oz - inf, z1 = oz + d + inf;

        var p = new Vector3[8]
        {
            new(x0, y0, z0), new(x1, y0, z0), new(x1, y1, z0), new(x0, y1, z0), // z0 면 (뒤)
            new(x0, y0, z1), new(x1, y0, z1), new(x1, y1, z1), new(x0, y1, z1), // z1 면 (앞)
        };

        float u = spec.Uv.X, v = spec.Uv.Y;

        // 표준 Box-UV 배치: top/bottom/우측/정면/좌측/후면
        Rect top = new(u + d, v, w, d);
        Rect bottom = new(u + d + w, v, w, d);
        Rect faceNegX = new(u, v + d, d, h);
        Rect front = new(u + d, v + d, w, h);
        Rect faceCPosX = new(u + d + w, v + d, d, h);
        Rect back = new(u + d + w + d, v + d, w, h);

        if (spec.MirrorX) (faceNegX, faceCPosX) = (faceCPosX, faceNegX);

        // +Y top / -Y bottom: 아래 순서는 외적(법선)이 각각 +Y/-Y로 나오도록 뒤집은 것 —
        // 처음엔 반대로 감겨서(법선이 안쪽을 향해서) 컬링에 걸려 머리/팔/다리 위아래가
        // 뚫려 보였다.
        AddFace(verts, indices, p[7], p[6], p[2], p[3], top, texW, texH, spec.MirrorX);
        // 아랫면은 위 순서(컬링 수정)를 그대로 90도 돌려 쓰면 감김 방향은 유지하면서
        // 텍스처의 앞/뒤가 뒤집혀 나왔다(머리 아랫면 위아래 반전 리포트) — 같은 컬링
        // 방향을 유지하는 사이클 상에서 시작점만 반대쪽으로 옮겨 바로잡았다.
        AddFace(verts, indices, p[5], p[4], p[0], p[1], bottom, texW, texH, spec.MirrorX);
        // +Z front (얼굴 등 정면 텍스처, 카메라 기본 방향과 마주보게)
        AddFace(verts, indices, p[4], p[5], p[6], p[7], front, texW, texH, spec.MirrorX);
        // -Z back
        AddFace(verts, indices, p[1], p[0], p[3], p[2], back, texW, texH, spec.MirrorX);
        // -X
        AddFace(verts, indices, p[0], p[4], p[7], p[3], faceNegX, texW, texH, spec.MirrorX);
        // +X
        AddFace(verts, indices, p[5], p[1], p[2], p[6], faceCPosX, texW, texH, spec.MirrorX);
    }

    private readonly record struct Rect(float U, float V, float W, float H);

    private static void AddFace(List<float> verts, List<uint> indices, Vector3 a, Vector3 b, Vector3 c, Vector3 dd,
        Rect uv, float texW, float texH, bool mirrorX)
    {
        uint baseIdx = (uint)(verts.Count / VertexFloats);

        // (a,b,c,d)는 각 면마다 "아래쪽 두 점 → 위쪽 두 점" 순서로 넘어온다
        // (예: 정면은 bottom-left,bottom-right,top-right,top-left). 반면 텍스처는
        // v가 작을수록 이미지 위쪽(이마 등)이므로, 3D 아래쪽 정점엔 v가 큰(이미지
        // 아래쪽, 턱 등) UV를 짝지어야 한다 — 처음엔 반대로 짝지어서 얼굴이
        // 위아래로 뒤집혀 나왔다.
        Vector2 uv0 = new(uv.U / texW, (uv.V + uv.H) / texH);
        Vector2 uv1 = new((uv.U + uv.W) / texW, (uv.V + uv.H) / texH);
        Vector2 uv2 = new((uv.U + uv.W) / texW, uv.V / texH);
        Vector2 uv3 = new(uv.U / texW, uv.V / texH);

        if (mirrorX) (uv0, uv1, uv2, uv3) = (uv1, uv0, uv3, uv2);

        AddVertex(verts, a, uv0);
        AddVertex(verts, b, uv1);
        AddVertex(verts, c, uv2);
        AddVertex(verts, dd, uv3);

        indices.Add(baseIdx); indices.Add(baseIdx + 1); indices.Add(baseIdx + 2);
        indices.Add(baseIdx); indices.Add(baseIdx + 2); indices.Add(baseIdx + 3);
    }

    private static void AddVertex(List<float> verts, Vector3 pos, Vector2 uv)
    {
        verts.Add(pos.X); verts.Add(pos.Y); verts.Add(pos.Z);
        verts.Add(uv.X); verts.Add(uv.Y);
    }

    // 모델 좌표계: 1 유닛 = 스킨 1픽셀. 발밑이 y=0, 정수리가 y=32.
    // slimArms: Mojang 프로필의 Skin.Model이 Alex(슬림)일 때 true. 슬림 모델은 팔이
    // 4px가 아니라 3px 폭이고, UV상 줄어든 1px는 몸통 반대쪽(바깥쪽) 가장자리에서
    // 빠진다 — 그래서 안쪽(몸통에 붙는 쪽) 모서리는 고정한 채 폭만 3으로 줄인다.
    // 이걸 4px로 고정해서 그리면 실제 팔 아트보다 1px 넓게 텍스처를 샘플링해서
    // 옆의 빈/투명 영역까지 걸치게 되고, 그 부분이 알파 디스카드로 투명하게 보인다.
    public static List<BoxSpec> BuildParts(bool legacyFormat, bool headOnly, bool slimArms = false)
    {
        var parts = new List<BoxSpec>();

        // 머리 8x8x8, 중심이 x=0,z=0 위에 오도록 x:-4..4, z:-4..4, y:24..32
        parts.Add(new BoxSpec(new Vector3(-4, 24, -4), new Vector3(8, 8, 8), new Vector2(0, 0)));
        parts.Add(new BoxSpec(new Vector3(-4, 24, -4), new Vector3(8, 8, 8), new Vector2(32, 0), Inflate: 0.5f));

        if (headOnly) return parts;

        // 몸통 8x12x4, y:12..24
        parts.Add(new BoxSpec(new Vector3(-4, 12, -2), new Vector3(8, 12, 4), new Vector2(16, 16)));
        if (!legacyFormat)
            parts.Add(new BoxSpec(new Vector3(-4, 12, -2), new Vector3(8, 12, 4), new Vector2(16, 32), Inflate: 0.25f));

        var armWidth = slimArms ? 3f : 4f;
        // 오른팔(캐릭터 기준 오른쪽 = 화면상 왼쪽, x음수 방향) y:12..24.
        // 안쪽 모서리(몸통과 맞닿는 x=-4)는 고정, 바깥쪽에서 폭을 줄인다.
        var rightArmOrigin = new Vector3(-4 - armWidth, 12, -2);
        parts.Add(new BoxSpec(rightArmOrigin, new Vector3(armWidth, 12, 4), new Vector2(40, 16)));
        // 왼팔: 신버전(64x64)은 전용 UV(32,48), 구버전(64x32)은 오른팔 UV를 좌우 반전해서 재사용
        var leftArmOrigin = new Vector3(4, 12, -2);
        parts.Add(legacyFormat
            ? new BoxSpec(leftArmOrigin, new Vector3(armWidth, 12, 4), new Vector2(40, 16), MirrorX: true)
            : new BoxSpec(leftArmOrigin, new Vector3(armWidth, 12, 4), new Vector2(32, 48)));

        if (!legacyFormat)
        {
            parts.Add(new BoxSpec(rightArmOrigin, new Vector3(armWidth, 12, 4), new Vector2(40, 32), Inflate: 0.25f));
            parts.Add(new BoxSpec(leftArmOrigin, new Vector3(armWidth, 12, 4), new Vector2(48, 48), Inflate: 0.25f));
        }

        // 오른다리 4x12x4, y:0..12
        parts.Add(new BoxSpec(new Vector3(-4, 0, -2), new Vector3(4, 12, 4), new Vector2(0, 16)));
        parts.Add(legacyFormat
            ? new BoxSpec(new Vector3(0, 0, -2), new Vector3(4, 12, 4), new Vector2(0, 16), MirrorX: true)
            : new BoxSpec(new Vector3(0, 0, -2), new Vector3(4, 12, 4), new Vector2(16, 48)));

        if (!legacyFormat)
        {
            parts.Add(new BoxSpec(new Vector3(-4, 0, -2), new Vector3(4, 12, 4), new Vector2(0, 32), Inflate: 0.25f));
            parts.Add(new BoxSpec(new Vector3(0, 0, -2), new Vector3(4, 12, 4), new Vector2(0, 48), Inflate: 0.25f));
        }

        return parts;
    }
}
