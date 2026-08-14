using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;
using Silk.NET.OpenGL;

namespace GrinLauncher.Rendering;

// OpenGlControlBase 기반 마인크래프트 휴머노이드 스킨 3D 뷰어.
// - HeadOnly=false: 전신 모델, 마우스 드래그로 자유 회전 + 휠 줌
// - HeadOnly=true: 머리 큐브만, 자동 Y축 회전, 인터랙션 없음(사이드바 장식용)
// GL 컨텍스트 생성/셰이더 컴파일 실패 시 InitFailed 이벤트를 올리고 렌더링을 멈춘다 —
// 호출부는 이 이벤트를 받아 기존 2D CroppedBitmap 방식으로 전환해야 한다.
//
// 드래그/휠 인터랙션은 이 컨트롤 자체가 아니라 호출부가 위에 얹는 투명 오버레이가
// 받아서 ApplyDrag/ApplyZoom으로 넘겨준다 — OpenGlControlBase에 PointerPressed/Moved를
// 직접 구독해봤는데 실기에서 전혀 안 들어왔다(호버조차 안 들어옴). GPU 합성 서피스라
// 일반 히트테스트 경로를 안 타는 것으로 보인다.
public class SkinViewerControl : OpenGlControlBase
{
    public static readonly StyledProperty<bool> HeadOnlyProperty =
        AvaloniaProperty.Register<SkinViewerControl, bool>(nameof(HeadOnly));

    public bool HeadOnly
    {
        get => GetValue(HeadOnlyProperty);
        set => SetValue(HeadOnlyProperty, value);
    }

    public event EventHandler<Exception>? InitFailed;

    private GL? _gl;
    private uint _vao, _vbo, _ebo, _program, _tex;
    private int _uMvp, _uTex;
    private int _indexCount;
    private bool _ready;

    private uint _bgVao, _bgVbo, _bgProgram, _bgTex;
    private bool _hasBgTex;

    private byte[]? _pendingSkinRgba;
    private int _pendingSkinW, _pendingSkinH;
    private bool _pendingSlimArms;
    private byte[]? _pendingBgRgba;
    private int _pendingBgW, _pendingBgH;
    private readonly object _pendingLock = new();

    private float _yaw = 0f, _pitch = 0f;
    private float? _distanceOverride;
    private (float R, float G, float B) _clearColor = (1f, 1f, 1f);

    // 전신(모델 높이 32유닛)과 머리만(8유닛)은 화면을 채우는 데 필요한 카메라 거리가
    // 전혀 다르다 — 전신 기준 거리를 그대로 쓰면 머리가 점처럼 작게 보인다.
    private float DefaultDistance => HeadOnly ? 16f : 70f;

    // Assets/background/*.png는 avares:// 임베디드 리소스로 번들되어 있다(csproj의
    // AvaloniaResource Include="Assets/**" 글롭에 포함).
    public static Bitmap LoadBundledBackground(string fileName)
    {
        using var stream = Avalonia.Platform.AssetLoader.Open(new Uri($"avares://GrinLauncher/Assets/background/{fileName}"));
        return new Bitmap(stream);
    }

    // design.md의 --surface 톤과 맞추기 위한 배경색. OpenGlControlBase의 알파 합성이
    // 플랫폼별로 신뢰할 수 없어(트리 뒤 콘텐츠와 블렌딩 여부 불확실) 매번 불투명하게
    // 채운다 — WebView 검토 때 겪었던 것과 같은 종류의 문제를 애초에 피하는 선택.
    public void SetBackgroundColor(byte r, byte g, byte b)
    {
        _clearColor = (r / 255f, g / 255f, b / 255f);
        RequestNextFrameRendering();
    }

    // 배경 이미지. 화면 전체를 채우는 평면(스킨/모델과 무관, MVP 없이 NDC 풀스크린
    // 쿼드)으로 모델보다 먼저 그린 뒤 깊이 테스트를 켜서 모델이 그 위에 자연스럽게
    // 겹치게 한다. 이미지 원본 비율은 무시하고 뷰어 영역에 꽉 채운다(가로세로 다소
    // 눌릴 수 있음 — 장식용 배경이라 여백 계산까지는 하지 않았다).
    public void SetBackgroundImage(Bitmap bitmap)
    {
        var w = bitmap.PixelSize.Width;
        var h = bitmap.PixelSize.Height;
        var buf = new byte[w * h * 4];
        unsafe
        {
            fixed (byte* p = buf)
                bitmap.CopyPixels(new PixelRect(0, 0, w, h), (IntPtr)p, buf.Length, w * 4);
        }

        lock (_pendingLock)
        {
            _pendingBgRgba = buf;
            _pendingBgW = w;
            _pendingBgH = h;
        }

        RequestNextFrameRendering();
    }

    // 오버레이가 드래그 델타(논리 픽셀)를 넘겨줄 때 호출. HeadOnly에서는 자동 회전만
    // 쓰므로 호출부에서부터 오버레이를 안 붙이면 그만이지만, 방어적으로도 무시한다.
    public void ApplyDrag(double dx, double dy)
    {
        if (HeadOnly) return;
        // 부호는 "손으로 모형을 잡고 돌린다"는 체감에 맞춘 것 — 오른쪽으로 드래그하면
        // 모형이 오른쪽으로 딸려 돌아야 한다. yaw += dx로 했더니 반대로 느껴진다는
        // 피드백을 받아 반전했다.
        _yaw -= (float)dx * 0.01f;
        _pitch = Math.Clamp(_pitch + (float)dy * 0.01f, -1.2f, 1.2f);
        RequestNextFrameRendering();
    }

    // 오버레이가 휠 델타를 넘겨줄 때 호출.
    public void ApplyZoom(double deltaY)
    {
        if (HeadOnly) return;
        const float min = 20f, max = 150f;
        _distanceOverride = Math.Clamp((_distanceOverride ?? DefaultDistance) - (float)deltaY * 4f, min, max);
        RequestNextFrameRendering();
    }

    // 스킨 텍스처 교체. 아직 GL 초기화 전이면 초기화 완료 후 자동 적용된다.
    // isSlim: Mojang 프로필의 Skin.Model이 Alex(슬림, 팔 3px)일 때 true. 항상 4px로
    // 그리면 3px 슬림 스킨에서 팔이 실제 아트보다 1px 넓게 텍스처를 샘플링해서
    // 옆의 빈 영역까지 걸치고, 그 부분이 투명하게(알파 디스카드) 보인다.
    public void SetSkin(Bitmap bitmap, bool isSlim = false)
    {
        var w = bitmap.PixelSize.Width;
        var h = bitmap.PixelSize.Height;
        var buf = new byte[w * h * 4];
        unsafe
        {
            fixed (byte* p = buf)
                bitmap.CopyPixels(new PixelRect(0, 0, w, h), (IntPtr)p, buf.Length, w * 4);
        }

        lock (_pendingLock)
        {
            _pendingSkinRgba = buf;
            _pendingSkinW = w;
            _pendingSkinH = h;
            _pendingSlimArms = isSlim;
        }

        RequestNextFrameRendering();
    }

    protected override unsafe void OnOpenGlInit(GlInterface gl)
    {
        try
        {
            _gl = GL.GetApi(gl.GetProcAddress);
            var g = _gl;

            const string vsrc = """
                #version 330 core
                layout(location=0) in vec3 aPos;
                layout(location=1) in vec2 aUv;
                uniform mat4 uMvp;
                out vec2 vUv;
                void main() { gl_Position = uMvp * vec4(aPos, 1.0); vUv = aUv; }
                """;
            const string fsrc = """
                #version 330 core
                in vec2 vUv;
                out vec4 FragColor;
                uniform sampler2D uTex;
                void main() {
                    vec4 c = texture(uTex, vUv);
                    if (c.a < 0.1) discard;
                    FragColor = c;
                }
                """;

            uint vs = g.CreateShader(ShaderType.VertexShader);
            g.ShaderSource(vs, vsrc);
            g.CompileShader(vs);
            CheckShader(g, vs, "vertex");

            uint fs = g.CreateShader(ShaderType.FragmentShader);
            g.ShaderSource(fs, fsrc);
            g.CompileShader(fs);
            CheckShader(g, fs, "fragment");

            _program = g.CreateProgram();
            g.AttachShader(_program, vs);
            g.AttachShader(_program, fs);
            g.LinkProgram(_program);
            g.GetProgram(_program, ProgramPropertyARB.LinkStatus, out var linkStatus);
            if (linkStatus == 0) throw new InvalidOperationException("link error: " + g.GetProgramInfoLog(_program));
            g.DeleteShader(vs);
            g.DeleteShader(fs);

            _uMvp = g.GetUniformLocation(_program, "uMvp");
            _uTex = g.GetUniformLocation(_program, "uTex");

            BuildMesh(g, legacyFormat: false, slimArms: false); // 스킨 로드 시 실제 포맷/모델에 맞춰 재생성

            _tex = g.GenTexture();
            g.BindTexture(TextureTarget.Texture2D, _tex);
            g.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
            g.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
            g.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
            g.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
            // 텍스처가 아직 없을 때 빈 화면 대신 회색으로 채워, 스킨 로드 전에도
            // 형태(정육면체 다발)는 보이게 한다.
            var placeholder = new byte[64 * 64 * 4];
            Array.Fill(placeholder, (byte)180);
            fixed (byte* p = placeholder)
                g.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, 64, 64, 0, PixelFormat.Rgba, PixelType.UnsignedByte, p);

            g.Enable(EnableCap.DepthTest);
            g.Enable(EnableCap.CullFace);
            g.CullFace(TriangleFace.Back);

            InitBackgroundQuad(g);

            _ready = true;
        }
        catch (Exception ex)
        {
            _ready = false;
            Dispatcher.UIThread.Post(() => InitFailed?.Invoke(this, ex));
        }
    }

    private void BuildMesh(GL g, bool legacyFormat, bool slimArms)
    {
        var verts = new List<float>();
        var indices = new List<uint>();
        float texW = legacyFormat ? 64 : 64;
        float texH = legacyFormat ? 32 : 64;

        foreach (var box in SkinModelBuilder.BuildParts(legacyFormat, HeadOnly, slimArms))
            SkinModelBuilder.AddBox(verts, indices, box, texW, texH);

        _indexCount = indices.Count;

        if (_vao == 0) _vao = g.GenVertexArray();
        g.BindVertexArray(_vao);

        if (_vbo == 0) _vbo = g.GenBuffer();
        g.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        var vArr = verts.ToArray();
        unsafe
        {
            fixed (float* v = vArr)
                g.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vArr.Length * sizeof(float)), v, BufferUsageARB.StaticDraw);
        }

        if (_ebo == 0) _ebo = g.GenBuffer();
        g.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
        var iArr = indices.ToArray();
        unsafe
        {
            fixed (uint* i = iArr)
                g.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(iArr.Length * sizeof(uint)), i, BufferUsageARB.StaticDraw);
        }

        const int stride = SkinModelBuilder.VertexFloats * sizeof(float);
        unsafe
        {
            g.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
            g.EnableVertexAttribArray(0);
            g.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));
            g.EnableVertexAttribArray(1);
        }
    }

    private unsafe void InitBackgroundQuad(GL g)
    {
        const string vsrc = """
            #version 330 core
            layout(location=0) in vec2 aPos;
            layout(location=1) in vec2 aUv;
            out vec2 vUv;
            void main() { gl_Position = vec4(aPos, 0.0, 1.0); vUv = aUv; }
            """;
        const string fsrc = """
            #version 330 core
            in vec2 vUv;
            out vec4 FragColor;
            uniform sampler2D uBgTex;
            void main() { FragColor = texture(uBgTex, vUv); }
            """;

        uint vs = g.CreateShader(ShaderType.VertexShader);
        g.ShaderSource(vs, vsrc);
        g.CompileShader(vs);
        CheckShader(g, vs, "bg vertex");

        uint fs = g.CreateShader(ShaderType.FragmentShader);
        g.ShaderSource(fs, fsrc);
        g.CompileShader(fs);
        CheckShader(g, fs, "bg fragment");

        _bgProgram = g.CreateProgram();
        g.AttachShader(_bgProgram, vs);
        g.AttachShader(_bgProgram, fs);
        g.LinkProgram(_bgProgram);
        g.GetProgram(_bgProgram, ProgramPropertyARB.LinkStatus, out var linkStatus);
        if (linkStatus == 0) throw new InvalidOperationException("bg link error: " + g.GetProgramInfoLog(_bgProgram));
        g.DeleteShader(vs);
        g.DeleteShader(fs);

        // 뷰어 영역을 꽉 채우는 풀스크린 쿼드. pos는 NDC(-1..1), uv는 이미지 원점(위)이
        // 화면 위쪽에 오도록 y를 맞춰뒀다 — 모델 텍스처와 같은 CopyPixels 규약(버퍼
        // 0번 행 = 이미지 맨 위)을 그대로 따른다.
        float[] quad =
        {
            -1f, -1f, 0f, 1f,
             1f, -1f, 1f, 1f,
             1f,  1f, 1f, 0f,
            -1f,  1f, 0f, 0f,
        };
        uint[] quadIndices = { 0, 1, 2, 0, 2, 3 };

        _bgVao = g.GenVertexArray();
        g.BindVertexArray(_bgVao);

        _bgVbo = g.GenBuffer();
        g.BindBuffer(BufferTargetARB.ArrayBuffer, _bgVbo);
        fixed (float* v = quad)
            g.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(quad.Length * sizeof(float)), v, BufferUsageARB.StaticDraw);

        var bgEbo = g.GenBuffer();
        g.BindBuffer(BufferTargetARB.ElementArrayBuffer, bgEbo);
        fixed (uint* i = quadIndices)
            g.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(quadIndices.Length * sizeof(uint)), i, BufferUsageARB.StaticDraw);

        const int stride = 4 * sizeof(float);
        g.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, (void*)0);
        g.EnableVertexAttribArray(0);
        g.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, (void*)(2 * sizeof(float)));
        g.EnableVertexAttribArray(1);

        _bgTex = g.GenTexture();
        g.BindTexture(TextureTarget.Texture2D, _bgTex);
        g.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        g.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        g.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        g.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
    }

    private static void CheckShader(GL g, uint shader, string label)
    {
        g.GetShader(shader, ShaderParameterName.CompileStatus, out var status);
        if (status == 0) throw new InvalidOperationException($"{label} shader compile error: " + g.GetShaderInfoLog(shader));
    }

    protected override unsafe void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (_gl is null || !_ready) return;
        var g = _gl;

        try
        {
            byte[]? pendingSkin;
            int pw, ph;
            bool slimArms;
            byte[]? pendingBg;
            int bgw, bgh;
            lock (_pendingLock)
            {
                pendingSkin = _pendingSkinRgba;
                pw = _pendingSkinW;
                ph = _pendingSkinH;
                slimArms = _pendingSlimArms;
                _pendingSkinRgba = null;
                pendingBg = _pendingBgRgba;
                bgw = _pendingBgW;
                bgh = _pendingBgH;
                _pendingBgRgba = null;
            }
            if (pendingSkin is not null)
            {
                g.BindTexture(TextureTarget.Texture2D, _tex);
                fixed (byte* p = pendingSkin)
                    g.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, (uint)pw, (uint)ph, 0, PixelFormat.Rgba, PixelType.UnsignedByte, p);
                BuildMesh(g, legacyFormat: ph <= 32, slimArms: slimArms);
            }
            if (pendingBg is not null)
            {
                g.BindTexture(TextureTarget.Texture2D, _bgTex);
                fixed (byte* p = pendingBg)
                    g.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, (uint)bgw, (uint)bgh, 0, PixelFormat.Rgba, PixelType.UnsignedByte, p);
                _hasBgTex = true;
            }

            g.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)fb);
            // Bounds는 논리(DIP) 픽셀이지만 실제 프레임버퍼는 물리 픽셀 크기 —
            // Retina(스케일링>1) 화면에서 이걸 빼먹으면 뷰포트가 실제 프레임버퍼의
            // 일부만 채우고 나머지는 클리어 색으로 남는다(작을수록 눈에 띔).
            var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
            var width = Math.Max(1, (int)(Bounds.Width * scaling));
            var height = Math.Max(1, (int)(Bounds.Height * scaling));
            g.Viewport(0, 0, (uint)width, (uint)height);
            g.ClearColor(_clearColor.R, _clearColor.G, _clearColor.B, 1f);
            g.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));

            if (_hasBgTex)
            {
                // 배경은 모델과 무관한 풀스크린 쿼드라 깊이테스트를 꺼서 그린다 —
                // 켜둔 채로 그리면 이후 모델이 배경보다 항상 "더 가깝다"는 보장이
                // 없어 깜빡이거나 가려질 수 있다. 모델은 바로 아래에서 깊이테스트를
                // 다시 켜고 그리므로 배경 위에 자연스럽게 겹쳐진다.
                g.Disable(EnableCap.DepthTest);
                g.UseProgram(_bgProgram);
                g.ActiveTexture(TextureUnit.Texture0);
                g.BindTexture(TextureTarget.Texture2D, _bgTex);
                g.Uniform1(g.GetUniformLocation(_bgProgram, "uBgTex"), 0);
                g.BindVertexArray(_bgVao);
                g.DrawElements(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedInt, (void*)0);
                g.Enable(EnableCap.DepthTest);
            }

            if (HeadOnly)
            {
                _yaw += 0.012f;
            }

            // 모델 원점(발밑 y=0 ~ 정수리 y=32) 기준, 카메라가 바라볼 중심을 모드에 맞게 이동
            var target = HeadOnly ? new Vector3(0, 28, 0) : new Vector3(0, 16, 0);

            var distance = _distanceOverride ?? DefaultDistance;
            var eye = target + new Vector3(
                MathF.Sin(_yaw) * MathF.Cos(_pitch),
                MathF.Sin(_pitch),
                MathF.Cos(_yaw) * MathF.Cos(_pitch)) * distance;

            var view = Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY);
            var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 5, (float)width / height, 1f, 500f);
            var mvp = view * proj;

            g.UseProgram(_program);
            g.ActiveTexture(TextureUnit.Texture0);
            g.BindTexture(TextureTarget.Texture2D, _tex);
            g.Uniform1(_uTex, 0);
            // System.Numerics.Matrix4x4 is row-major (row-vector v*M convention); GLSL's
            // column-vector M*v needs the transpose, and glUniformMatrix4fv(transpose:false)
            // reading our row-major floats as column-major *is* that transpose. Passing
            // true here re-transposes back to the wrong matrix (silently "looks okay" for
            // simple rotation-only tests since R^T ~= R^-1 still rotates, but breaks once
            // LookAt/Perspective are combined — exactly what stage-3 exposed).
            g.UniformMatrix4(_uMvp, 1, false, (float*)&mvp);
            g.BindVertexArray(_vao);
            g.DrawElements(PrimitiveType.Triangles, (uint)_indexCount, DrawElementsType.UnsignedInt, (void*)0);
        }
        catch (Exception ex)
        {
            // 렌더 루프에서 예외가 나면 이후 프레임이 통째로 안 그려질 수 있어 방어적으로
            // 잡아서 로그만 남긴다 — 스킨 로딩 문제를 진단할 때 실제로 도움이 됐다.
            Console.WriteLine("[skinviewer] OnOpenGlRender exception: " + ex);
        }

        RequestNextFrameRendering();
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        if (_gl is null) return;
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteBuffer(_ebo);
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteTexture(_tex);
        _gl.DeleteProgram(_program);
        _gl.DeleteBuffer(_bgVbo);
        _gl.DeleteVertexArray(_bgVao);
        _gl.DeleteTexture(_bgTex);
        _gl.DeleteProgram(_bgProgram);
        _ready = false;
    }
}
