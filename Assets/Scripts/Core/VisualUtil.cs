using UnityEngine;
using UnityEngine.Rendering;
namespace Game.Core
{
    // Shared materials and shadow off. Loads project shaders so builds keep them.
    // Contains shared helpers for runtime-created materials, effects and render settings.
    public static class VisualUtil
    {
        private static Shader additiveShader;
        private static Shader unlitColorShader;
        private static Shader unlitTextureShader;
        private static bool shadersReady;
        private static Sprite whiteSprite;
        private static Sprite hexSprite;
        private static Sprite circleSprite;
        private static Font uiFont;

        public static void HideShadows(Renderer renderer)
        {
            // Turns off casting and receiving together because runtime effects should not create expensive shadows.
            if (renderer == null)
            {
                return;
            }

            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        public static void HideShadows(GameObject root)
        {
            // Applies the same no-shadow rule to every renderer on an imported model or generated effect.
            if (root == null)
            {
                return;
            }

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);

            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                HideShadows(renderers[rendererIndex]);
            }
        }

        public static Material MakeAdditive(Color colour)
        {
            if (GraphicsSettings.currentRenderPipeline != null)
            {
                var effect = Game.Graphics.GraphicsMaterials.Unlit("Effect", colour, true);
                effect.SetFloat("_SoftEdge", 1f);
                return effect;
            }

            // Builds a glowing material using the project's preferred shader, with safe Unity shader fallbacks.
            EnsureShaders();
            Shader shader = additiveShader;
            if (shader == null)
            {
                // Falls back to a simple material if the additive shader was removed from a build.
                shader = Shader.Find("Sprites/Default");
            }

            if (shader == null)
            {
                return FallbackMaterial(colour);
            }

            Material material = new Material(shader);
            SetColour(material, colour);
            return material;
        }

        public static Material MakeUnlit(Color colour)
        {
            if (GraphicsSettings.currentRenderPipeline != null)
            {
                return Game.Graphics.GraphicsMaterials.Unlit("Unlit", colour);
            }

            // Creates a flat-colour material for UI-like geometry that should ignore scene lighting.
            EnsureShaders();
            Shader shader = unlitColorShader;
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            if (shader == null)
            {
                return FallbackMaterial(colour);
            }

            Material material = new Material(shader);
            SetColour(material, colour);
            return material;
        }

        public static Material MakeUnlitTexture(Texture texture)
        {
            if (GraphicsSettings.currentRenderPipeline != null)
            {
                var hdrpMaterial = Game.Graphics.GraphicsMaterials.Unlit("Textured effect", Color.white);
                hdrpMaterial.SetTexture("_MainTex", texture);
                return hdrpMaterial;
            }

            // Creates an unlit textured material for imported art that must remain visible in dark space.
            EnsureShaders();
            Shader shader = unlitTextureShader;
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Texture");
            }

            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            if (shader == null)
            {
                return FallbackMaterial(Color.white);
            }

            Material material = new Material(shader);
            SetColour(material, Color.white);
            if (texture != null)
            {
                // Assigns the texture using both common material paths for shader compatibility.
                material.mainTexture = texture;
                if (material.HasProperty("_MainTex"))
                {
                    material.SetTexture("_MainTex", texture);
                }
            }

            return material;
        }

        public static Sprite WhiteSprite()
        {
            // Creates and caches a tiny white sprite used as the base for coloured runtime UI images.
            if (whiteSprite != null)
            {
                return whiteSprite;
            }

            Texture2D texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Point;
            Color[] pixels = new Color[16];

            // Fills every pixel white so UI tint colours can control the final appearance.
            for (int pixelIndex = 0; pixelIndex < pixels.Length; pixelIndex++)
            {
                pixels[pixelIndex] = Color.white;
            }

            texture.SetPixels(pixels);
            texture.Apply();
            whiteSprite = Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f), 4f);
            whiteSprite.name = "UiWhite";
            return whiteSprite;
        }

        public static Sprite CircleSprite()
        {
            // Draws a soft circle pixel-by-pixel once, then reuses the cached sprite for all circle UI elements.
            if (circleSprite != null)
            {
                return circleSprite;
            }

            int size = 128;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            float centreX = (size - 1) * 0.5f;
            float centreY = (size - 1) * 0.5f;
            float radius = size * 0.48f;

            for (int pixelY = 0; pixelY < size; pixelY++)
            {
                for (int pixelX = 0; pixelX < size; pixelX++)
                {
                    float horizontalOffset = pixelX - centreX;
                    float verticalOffset = pixelY - centreY;
                    float distanceFromCentre = Mathf.Sqrt(horizontalOffset * horizontalOffset + verticalOffset * verticalOffset);

                    // Calculates distance from the texture centre to decide whether this pixel is inside the circle.
                    Color pixel = Color.clear;
                    if (distanceFromCentre <= radius)
                    {
                        float edge = Mathf.Clamp01((radius - distanceFromCentre) * 3f);

                        // Fades alpha near the edge to avoid a harsh jagged outline at small UI sizes.
                        pixel = new Color(1f, 1f, 1f, edge);
                    }

                    texture.SetPixel(pixelX, pixelY, pixel);
                }
            }

            texture.Apply();
            circleSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
            circleSprite.name = "UiCircle";
            return circleSprite;
        }

        public static Sprite HexSprite()
        {
            // Draws a pointy hexagon sprite at runtime for skill-tree nodes and related UI elements.
            if (hexSprite != null)
            {
                return hexSprite;
            }

            int size = 128;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            float radius = size * 0.48f;
            float centreX = (size - 1) * 0.5f;
            float centreY = (size - 1) * 0.5f;

            for (int pixelY = 0; pixelY < size; pixelY++)
            {
                for (int pixelX = 0; pixelX < size; pixelX++)
                {
                    float pixelOffsetX = pixelX - centreX;
                    float pixelOffsetY = pixelY - centreY;
                    Color pixel = Color.clear;
                    if (InsidePointyHex(pixelOffsetX, pixelOffsetY, radius))
                    {
                        // Colours only pixels inside the hex test; outside pixels remain transparent.
                        pixel = Color.white;
                    }

                    texture.SetPixel(pixelX, pixelY, pixel);
                }
            }

            texture.Apply();
            hexSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
            hexSprite.name = "UiHex";
            return hexSprite;
        }

        public static Font UiFont()
        {
            // Tries several common operating-system fonts so runtime-created text remains visible across machines.
            if (uiFont != null)
            {
                return uiFont;
            }

            uiFont = Resources.Load<Font>("UI/Fonts/Roboto-Bold");
            if (uiFont != null)
            {
                return uiFont;
            }

            string[] names = { "Arial", "Segoe UI", "Tahoma", "Verdana", "Helvetica" };
            uiFont = Font.CreateDynamicFontFromOSFont(names, 24);
            return uiFont;
        }

        private static void EnsureShaders()
        {
            // Loads project shaders once, then tries Unity fallbacks in order if build stripping removed one.
            if (shadersReady)
            {
                return;
            }

            shadersReady = true;
            additiveShader = Resources.Load<Shader>("Shaders/GameAdditive");
            unlitColorShader = Resources.Load<Shader>("Shaders/GameUnlitColor");
            unlitTextureShader = Resources.Load<Shader>("Shaders/GameUnlitTexture");
            if (additiveShader == null)
            {
                // Uses older particle shader names as a final compatibility path for additive effects.
                additiveShader = Shader.Find("Game/Additive");
            }

            if (unlitColorShader == null)
            {
                unlitColorShader = Shader.Find("Game/UnlitColor");
            }

            if (unlitTextureShader == null)
            {
                unlitTextureShader = Shader.Find("Game/UnlitTexture");
            }

            if (additiveShader == null)
            {
                additiveShader = Shader.Find("Legacy Shaders/Particles/Additive");
            }

            if (additiveShader == null)
            {
                additiveShader = Shader.Find("Mobile/Particles/Additive");
            }

            if (additiveShader == null)
            {
                additiveShader = Shader.Find("Particles/Standard Unlit");
            }
        }

        private static void SetColour(Material material, Color colour)
        {
            // Writes multiple common colour property names because different shaders expose different fields.
            if (material == null)
            {
                return;
            }

            material.color = colour;
            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", colour);
            }

            if (material.HasProperty("_TintColor"))
            {
                material.SetColor("_TintColor", colour);
            }
        }

        private static Material FallbackMaterial(Color colour)
        {
            // Uses simple Sprite or UI shaders only when the specialised visual shader cannot be found.
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("UI/Default");
            }

            if (shader == null)
            {
                return null;
            }

            Material material = new Material(shader);
            SetColour(material, colour);
            return material;
        }

        private static bool InsidePointyHex(float coordinateX, float coordinateY, float radius)
        {
            // Mirrors coordinates into one quadrant, then tests the sloped hex edge with a simple line equation.
            coordinateX = Mathf.Abs(coordinateX);
            coordinateY = Mathf.Abs(coordinateY);
            if (coordinateX > radius * 0.8660254f)
            {
                // Rejects points outside the half-width of a regular pointy hexagon.
                return false;
            }

            return coordinateY <= radius - coordinateX * 0.57735027f;
        }
    }
}
