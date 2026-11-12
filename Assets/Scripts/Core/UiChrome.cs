using UnityEngine;
using UnityEngine.UI;
namespace Game.Core
{
    // Builds reusable sci-fi UI shapes and sprites in code so every menu shares one style.
    public static class UiChrome
    {
        public static readonly Color MetalDark = Hex(0x071D28);
        public static readonly Color Metal = Hex(0x0C2B37);
        public static readonly Color MetalLight = Hex(0x677F8C);
        public static readonly Color NeonRed = Hex(0xFF6E79);
        public static readonly Color NeonBlue = Hex(0x4CEAF2);
        public static readonly Color Gold = Hex(0xFFB547);
        public static readonly Color Ink = Hex(0x031119);
        public static readonly Color DimText = Hex(0xA5BDC8);
        public static readonly Color TextColor = Hex(0xF5FAFC);
        public static readonly Color Mobility = Hex(0xC999FF);
        public static readonly Color Support = Hex(0x56E8D8);
        public static readonly Color Utility = Hex(0x93D7FF);
        private static readonly System.Collections.Generic.Dictionary<string, Sprite> suppliedIcons =
            new System.Collections.Generic.Dictionary<string, Sprite>();

        private static Color Hex(uint packedRgb)
        {
            return new Color(((packedRgb >> 16) & 255) / 255f, ((packedRgb >> 8) & 255) / 255f, (packedRgb & 255) / 255f);
        }

        public static Sprite Icon(string name, bool accent = false)
        {
            string path = "UI/Icons/" + (accent ? "Accent/" : "White/") + name;
            if (!suppliedIcons.TryGetValue(path, out Sprite sprite))
            {
                sprite = Resources.Load<Sprite>(path);
                if (sprite != null)
                {
                    suppliedIcons[path] = sprite;
                }
            }

            return sprite != null ? sprite : SkillIcon('?');
        }

        public static Image Panel(Transform parent, string name, Color border)
        {
            Image fill = MakeImage(parent, name, ChamferFill(), MetalDark);
            fill.type = Image.Type.Sliced;
            Image edge = MakeImage(fill.transform, "Border", ChamferRing(), border);
            edge.type = Image.Type.Sliced;
            Stretch(edge.rectTransform);
            return fill;
        }

        public static void StyleButton(Button button, Color accent)
        {
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = Color.Lerp(Color.white, accent, 0.35f);
            colors.selectedColor = accent;
            colors.pressedColor = Color.Lerp(accent, Color.white, 0.5f);
            colors.disabledColor = new Color(0.4f, 0.5f, 0.55f, 0.7f);
            button.colors = colors;
        }
        private static Sprite octagonSprite;
        private static Sprite octagonRingSprite;
        private static Sprite hexRingSprite;
        private static Sprite scanlineSprite;
        private static Sprite hatchSprite;
        private static Sprite glowSprite;
        private static Sprite slantSprite;
        private static Sprite shipSprite;
        private static Sprite lockSprite;
        private static Sprite chevronSprite;
        private static Sprite chamferFillSprite;
        private static Sprite chamferRingSprite;
        private static Sprite gridSprite;
        private static Sprite ventsSprite;
        private static readonly System.Collections.Generic.Dictionary<char, Sprite> iconCache =
            new System.Collections.Generic.Dictionary<char, Sprite>();

        public static Image MakeImage(Transform parent, string name, Sprite sprite, Color colour)
        {
            // Creates a centred UI image with pointer events disabled so decorative layers do not block buttons.
            GameObject sceneObject = new GameObject(name);
            sceneObject.transform.SetParent(parent, false);
            Image image = sceneObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = colour;
            image.raycastTarget = false;
            RectTransform rect = image.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            return image;
        }

        public static Image MakeBox(Transform parent, string name, Color colour)
        {
            // Reuses the shared white sprite, allowing the Image colour to create a solid UI rectangle.
            return MakeImage(parent, name, VisualUtil.WhiteSprite(), colour);
        }

        public static Text MakeText(Transform parent, string name, string value, int size, TextAnchor align, Color colour)
        {
            // Creates consistently styled runtime text using the shared fallback-safe UI font.
            GameObject sceneObject = new GameObject(name);
            sceneObject.transform.SetParent(parent, false);
            Text text = sceneObject.AddComponent<Text>();
            text.font = VisualUtil.UiFont();
            text.fontSize = size;
            text.fontStyle = FontStyle.Bold;
            text.alignment = align;
            text.color = colour;
            text.text = value;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        public static void Stretch(RectTransform rect)
        {
            // Anchors a UI element to all four parent edges so it fills its parent without manual size values.
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        public static void Place(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            // Applies the common anchor, pivot, position and size values used by runtime-built UI panels.
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        public static Sprite Octagon()
        {
            // Returns the cached octagon after its first procedural draw.
            if (octagonSprite != null)
            {
                return octagonSprite;
            }

            octagonSprite = MakeShapeSprite("UiOctagon", 128, InsideOctagon);
            return octagonSprite;
        }

        public static Sprite OctagonRing()
        {
            // Returns the cached octagon border after its first procedural draw.
            if (octagonRingSprite != null)
            {
                return octagonRingSprite;
            }

            octagonRingSprite = MakeRingSprite("UiOctagonRing", 128, InsideOctagon, 0.82f);
            return octagonRingSprite;
        }

        public static Sprite HexRing()
        {
            // Returns the cached hexagon border after its first procedural draw.
            if (hexRingSprite != null)
            {
                return hexRingSprite;
            }

            hexRingSprite = MakeRingSprite("UiHexRing", 128, InsideHex, 0.78f);
            return hexRingSprite;
        }

        public static Sprite Scanlines()
        {
            // Draws a repeatable small texture where every fourth row is brighter to create a scanline effect.
            if (scanlineSprite != null)
            {
                return scanlineSprite;
            }

            int width = 8;
            int height = 16;
            Texture2D texture = MakeTex(width, height);

            for (int pixelY = 0; pixelY < height; pixelY++)
            {
                float opacity = 0.08f;
                if ((pixelY % 4) == 0)
                {
                    opacity = 0.35f;
                }

                for (int pixelX = 0; pixelX < width; pixelX++)
                {
                    texture.SetPixel(pixelX, pixelY, new Color(1f, 1f, 1f, opacity));
                }
            }

            texture.Apply();
            texture.wrapMode = TextureWrapMode.Repeat;
            scanlineSprite = Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), height);
            scanlineSprite.name = "UiScanlines";
            return scanlineSprite;
        }

        public static Sprite Hatch()
        {
            // Draws diagonal semi-transparent marks using a repeating x-plus-y pattern for damaged UI pips.
            if (hatchSprite != null)
            {
                return hatchSprite;
            }

            int size = 32;
            Texture2D texture = MakeTex(size, size);

            for (int pixelY = 0; pixelY < size; pixelY++)
            {
                for (int pixelX = 0; pixelX < size; pixelX++)
                {
                    float opacity = 0.08f;
                    if (((pixelX + pixelY) % 6) == 0)
                    {
                        opacity = 0.28f;
                    }

                    texture.SetPixel(pixelX, pixelY, new Color(1f, 1f, 1f, opacity));
                }
            }

            texture.Apply();
            hatchSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
            hatchSprite.name = "UiHatch";
            return hatchSprite;
        }

        public static Sprite SoftGlow()
        {
            // Calculates radial distance from the texture centre to create a soft square-faded glow sprite.
            if (glowSprite != null)
            {
                return glowSprite;
            }

            int size = 128;
            Texture2D texture = MakeTex(size, size);
            float centreX = (size - 1) * 0.5f;
            float centreY = (size - 1) * 0.5f;
            float radius = size * 0.5f;

            for (int pixelY = 0; pixelY < size; pixelY++)
            {
                for (int pixelX = 0; pixelX < size; pixelX++)
                {
                    float horizontalOffset = (pixelX - centreX) / radius;
                    float verticalOffset = (pixelY - centreY) / radius;
                    float normalizedDistance = Mathf.Sqrt(horizontalOffset * horizontalOffset + verticalOffset * verticalOffset);

                    // Uses Pythagoras to measure how far this pixel is from the glow centre.
                    float opacity = Mathf.Clamp01(1f - normalizedDistance);
                    opacity = opacity * opacity;

                    // Squares alpha so brightness falls away more gently near the centre and faster at the edge.
                    texture.SetPixel(pixelX, pixelY, new Color(1f, 1f, 1f, opacity));
                }
            }

            texture.Apply();
            glowSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
            glowSprite.name = "UiGlow";
            return glowSprite;
        }

        public static Sprite Slant()
        {
            // Shifts the filled start point each row to make a slanted bar end for XP and health fills.
            if (slantSprite != null)
            {
                return slantSprite;
            }

            int width = 48;
            int height = 64;
            Texture2D texture = MakeTex(width, height);

            for (int pixelY = 0; pixelY < height; pixelY++)
            {
                float heightFraction = pixelY / (float)(height - 1);
                int start = Mathf.RoundToInt(heightFraction * 18f);

                for (int pixelX = 0; pixelX < width; pixelX++)
                {
                    Color pixel = Color.clear;
                    if (pixelX >= start)
                    {
                        pixel = Color.white;
                    }

                    texture.SetPixel(pixelX, pixelY, pixel);
                }
            }

            texture.Apply();
            slantSprite = Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0f, 0.5f), height);
            slantSprite.name = "UiSlant";
            return slantSprite;
        }

        public static Sprite ShipTop()
        {
            // Caches the ship icon so repeated HUD construction does not redraw the texture.
            if (shipSprite != null)
            {
                return shipSprite;
            }

            shipSprite = DrawIcon('S');
            return shipSprite;
        }

        public static Sprite Lock()
        {
            // Caches the lock icon because several locked skill and sector elements can use it.
            if (lockSprite != null)
            {
                return lockSprite;
            }

            lockSprite = DrawIcon('L');
            return lockSprite;
        }

        public static Sprite ChamferFill()
        {
            // Caches the chamfered button fill after generating it once.
            if (chamferFillSprite != null)
            {
                return chamferFillSprite;
            }

            chamferFillSprite = MakeChamferSprite("UiChamferFill", false);
            return chamferFillSprite;
        }

        public static Sprite ChamferRing()
        {
            // Caches the chamfered button outline after generating it once.
            if (chamferRingSprite != null)
            {
                return chamferRingSprite;
            }

            chamferRingSprite = MakeChamferSprite("UiChamferRing", true);
            return chamferRingSprite;
        }

        public static Sprite Grid()
        {
            if (gridSprite != null)
            {
                return gridSprite;
            }

            const int size = 64;
            Texture2D texture = MakeTex(size, size);
            texture.wrapMode = TextureWrapMode.Repeat;

            for (int pixelY = 0; pixelY < size; pixelY++)
            {
                for (int pixelX = 0; pixelX < size; pixelX++)
                {
                    texture.SetPixel(pixelX, pixelY, pixelX % 16 == 0 && pixelY % 16 == 0 ? new Color(1, 1, 1, 0.35f) : Color.clear);
                }
            }

            texture.Apply();
            gridSprite = Sprite.Create(texture, new Rect(0, 0, size, size), Vector2.one * 0.5f, 100f);
            gridSprite.name = "UiNavigationDots";
            return gridSprite;
        }

        public static Sprite Vents()
        {
            // Returns the cached vent detail after it has been drawn once.
            if (ventsSprite != null)
            {
                return ventsSprite;
            }

            // Alternates short line segments to create the vent pattern.
            int width = 10;
            int height = 48;
            Texture2D texture = MakeTex(width, height);

            for (int pixelY = 0; pixelY < height; pixelY++)
            {
                bool dash = (pixelY % 6) < 3;

                for (int pixelX = 0; pixelX < width; pixelX++)
                {
                    Color pixel = Color.clear;
                    if (dash && (pixelX == 2 || pixelX == 7))
                    {
                        pixel = Color.white;
                    }

                    texture.SetPixel(pixelX, pixelY, pixel);
                }
            }

            texture.Apply();
            ventsSprite = Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), height);
            ventsSprite.name = "UiVents";
            return ventsSprite;
        }

        private static Sprite MakeChamferSprite(string name, bool ring)
        {
            // Draws a clipped-corner rectangle pixel-by-pixel, optionally leaving only its border ring.
            int width = 96;
            int height = 64;
            int cornerCut = 10;
            int thick = 1;
            Texture2D texture = MakeTex(width, height);
            float centreX = (width - 1) * 0.5f;
            float centreY = (height - 1) * 0.5f;
            float halfWidth = width * 0.5f - 1f;
            float halfHeight = height * 0.5f - 1f;

            for (int pixelY = 0; pixelY < height; pixelY++)
            {
                for (int pixelX = 0; pixelX < width; pixelX++)
                {
                    float pixelOffsetX = pixelX - centreX;
                    float pixelOffsetY = pixelY - centreY;
                    bool outer = InsideChamfer(pixelOffsetX, pixelOffsetY, halfWidth, halfHeight, cornerCut);

                    // Tests whether the current pixel sits inside the large chamfered rectangle.
                    Color pixel = Color.clear;
                    if (ring)
                    {
                        bool inner = InsideChamfer(pixelOffsetX, pixelOffsetY, halfWidth - thick, halfHeight - thick, cornerCut - 1);

                        // Subtracts an inner shape from the outer shape to leave a constant-width border.
                        if (outer && !inner)
                        {
                            pixel = Color.white;
                        }
                    }
                    else if (outer)
                    {
                        pixel = Color.white;
                    }

                    texture.SetPixel(pixelX, pixelY, pixel);
                }
            }

            texture.Apply();
            Vector4 border = new Vector4(cornerCut + 2, cornerCut + 2, cornerCut + 2, cornerCut + 2);
            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, width, height),
                new Vector2(0.5f, 0.5f),
                64f,
                0,
                SpriteMeshType.FullRect,
                border);
            sprite.name = name;
            return sprite;
        }

        private static bool InsideChamfer(float coordinateX, float coordinateY, float halfWidth, float halfHeight, float cornerCut)
        {
            // Mirrors to one quadrant, then tests the diagonal corner cut using a simple line inequality.
            coordinateX = Mathf.Abs(coordinateX);
            coordinateY = Mathf.Abs(coordinateY);
            if (coordinateX > halfWidth || coordinateY > halfHeight)
            {
                return false;
            }

            return coordinateX + coordinateY <= halfWidth + halfHeight - cornerCut;
        }

        public static Sprite Chevron()
        {
            // Uses normalised pixel coordinates and distance bands to draw a compact downward chevron icon.
            if (chevronSprite != null)
            {
                return chevronSprite;
            }

            int size = 64;
            Texture2D texture = MakeTex(size, size);
            float centreX = (size - 1) * 0.5f;
            float centreY = (size - 1) * 0.5f;

            for (int pixelY = 0; pixelY < size; pixelY++)
            {
                for (int pixelX = 0; pixelX < size; pixelX++)
                {
                    float pixelOffsetX = (pixelX - centreX) / centreX;
                    float pixelOffsetY = (pixelY - centreY) / centreY;
                    Color pixel = Color.clear;
                    float edgeDistance = Mathf.Abs(pixelOffsetX) + pixelOffsetY * 1.4f;
                    if (pixelOffsetY < 0.55f && pixelOffsetY > -0.2f && edgeDistance < 0.85f && edgeDistance > 0.45f)
                    {
                        pixel = Color.white;
                    }

                    texture.SetPixel(pixelX, pixelY, pixel);
                }
            }

            texture.Apply();
            chevronSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
            chevronSprite.name = "UiChevron";
            return chevronSprite;
        }

        public static Sprite SkillIcon(char mark)
        {
            // Caches generated icons by their mark character so the same skill symbol is drawn only once.
            if (iconCache.ContainsKey(mark))
            {
                return iconCache[mark];
            }

            Sprite sprite = DrawIcon(mark);
            iconCache[mark] = sprite;
            return sprite;
        }

        private static Sprite DrawIcon(char mark)
        {
            // Composes simple rectangles, triangles and circles into a readable symbol for each skill category.
            int size = 128;
            Texture2D texture = MakeTex(size, size);
            Color branchColor = Color.white;

            // Chooses the simple drawing recipe that matches this skill or UI symbol.
            if (mark == 'L')
            {
                FillRect(texture, 44, 28, 40, 36, branchColor);
                StrokeArc(texture, 64, 64, 22, branchColor);
            }
            else if (mark == 'S')
            {
                FillPoly(texture, new Vector2(64, 108), new Vector2(52, 70), new Vector2(76, 70), branchColor);
                FillRect(texture, 54, 40, 20, 34, branchColor);
                FillPoly(texture, new Vector2(64, 44), new Vector2(18, 28), new Vector2(48, 52), branchColor);
                FillPoly(texture, new Vector2(64, 44), new Vector2(110, 28), new Vector2(80, 52), branchColor);
                FillRect(texture, 58, 18, 12, 22, branchColor);
            }
            else if (mark == 'C' || mark == '*')
            {
                DrawRocket(texture, 44, 28, branchColor);
                DrawRocket(texture, 64, 36, branchColor);
                DrawRocket(texture, 84, 28, branchColor);
                StrokeCircle(texture, 64, 64, 46, branchColor);
            }
            else if (mark == 'B')
            {
                StrokeCircle(texture, 64, 64, 28, branchColor);
                FillRect(texture, 60, 86, 8, 18, branchColor);
                FillRect(texture, 48, 58, 32, 8, branchColor);
                FillRect(texture, 60, 46, 8, 32, branchColor);
            }
            else if (mark == '+' || mark == 'H')
            {
                FillRect(texture, 56, 28, 16, 72, branchColor);
                FillRect(texture, 28, 56, 72, 16, branchColor);
                StrokeCircle(texture, 64, 64, 48, branchColor);
            }
            else if (mark == 'O')
            {
                StrokeCircle(texture, 64, 64, 40, branchColor);
                StrokeCircle(texture, 64, 64, 28, branchColor);
            }
            else if (mark == '>')
            {
                FillPoly(texture, new Vector2(28, 32), new Vector2(28, 96), new Vector2(64, 64), branchColor);
                FillPoly(texture, new Vector2(58, 32), new Vector2(58, 96), new Vector2(100, 64), branchColor);
            }
            else if (mark == 'F' || mark == '2' || mark == '4' || mark == 'W')
            {
                FillRect(texture, 30, 78, 18, 8, branchColor);
                FillRect(texture, 54, 88, 18, 8, branchColor);
                FillRect(texture, 78, 78, 18, 8, branchColor);
                FillRect(texture, 42, 52, 12, 28, branchColor);
                FillRect(texture, 58, 40, 12, 40, branchColor);
                FillRect(texture, 74, 52, 12, 28, branchColor);
            }
            else if (mark == 'P' || mark == 'A' || mark == 'D')
            {
                FillPoly(texture, new Vector2(64, 104), new Vector2(40, 40), new Vector2(88, 40), branchColor);
                FillRect(texture, 58, 24, 12, 24, branchColor);
            }
            else
            {
                FillRect(texture, 36, 36, 56, 56, branchColor);
            }

            texture.Apply();
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
            sprite.name = "UiIcon_" + mark;
            return sprite;
        }

        private static void DrawRocket(Texture2D texture, int pixelX, int pixelY, Color branchColor)
        {
            // Combines a nose triangle, body rectangle and flame triangle to draw one small rocket icon.
            FillPoly(texture, new Vector2(pixelX, pixelY + 58), new Vector2(pixelX - 8, pixelY + 18), new Vector2(pixelX + 8, pixelY + 18), branchColor);
            FillRect(texture, pixelX - 5, pixelY + 8, 10, 18, branchColor);
            FillPoly(texture, new Vector2(pixelX, pixelY), new Vector2(pixelX - 7, pixelY + 12), new Vector2(pixelX + 7, pixelY + 12), branchColor);
        }

        private static Sprite MakeShapeSprite(string name, int size, ShapeTest test)
        {
            // Runs a supplied inside-shape test for each pixel to turn reusable maths into a white UI sprite.
            Texture2D texture = MakeTex(size, size);
            float centreX = (size - 1) * 0.5f;
            float centreY = (size - 1) * 0.5f;
            float radius = size * 0.48f;

            for (int pixelY = 0; pixelY < size; pixelY++)
            {
                for (int pixelX = 0; pixelX < size; pixelX++)
                {
                    Color pixel = Color.clear;

                    // Colours this pixel only when the supplied shape test says it is inside the shape.
                    if (test(pixelX - centreX, pixelY - centreY, radius))
                    {
                        pixel = Color.white;
                    }

                    texture.SetPixel(pixelX, pixelY, pixel);
                }
            }

            texture.Apply();
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
            sprite.name = name;
            return sprite;
        }

        private static Sprite MakeRingSprite(string name, int size, ShapeTest test, float inner)
        {
            // Draws pixels inside the outer shape but outside a scaled inner copy, leaving a ring border.
            Texture2D texture = MakeTex(size, size);
            float centreX = (size - 1) * 0.5f;
            float centreY = (size - 1) * 0.5f;
            float radius = size * 0.48f;

            for (int pixelY = 0; pixelY < size; pixelY++)
            {
                for (int pixelX = 0; pixelX < size; pixelX++)
                {
                    float pixelOffsetX = pixelX - centreX;
                    float pixelOffsetY = pixelY - centreY;
                    Color pixel = Color.clear;

                    // Keeps pixels inside the outer shape but removes the scaled inner shape to form a border.
                    if (test(pixelOffsetX, pixelOffsetY, radius) && !test(pixelOffsetX, pixelOffsetY, radius * inner))
                    {
                        pixel = Color.white;
                    }

                    texture.SetPixel(pixelX, pixelY, pixel);
                }
            }

            texture.Apply();
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
            sprite.name = name;
            return sprite;
        }

        // Represents a reusable mathematical test for whether a point belongs inside a generated shape.
        private delegate bool ShapeTest(float coordinateX, float coordinateY, float radius);

        private static bool InsideOctagon(float coordinateX, float coordinateY, float radius)
        {
            // Mirrors coordinates and uses a diagonal sum limit to represent the cut corners of an octagon.
            coordinateX = Mathf.Abs(coordinateX);
            coordinateY = Mathf.Abs(coordinateY);
            if (coordinateX > radius || coordinateY > radius)
            {
                return false;
            }

            return coordinateX + coordinateY <= radius * 1.41421356f;
        }

        private static bool InsideHex(float coordinateX, float coordinateY, float radius)
        {
            // Tests a pointy hexagon using its half-width and sloped edge line after mirroring coordinates.
            coordinateX = Mathf.Abs(coordinateX);
            coordinateY = Mathf.Abs(coordinateY);
            if (coordinateX > radius * 0.8660254f)
            {
                return false;
            }

            return coordinateY <= radius - coordinateX * 0.57735027f;
        }

        private static Texture2D MakeTex(int width, int height)
        {
            // Creates a transparent bilinear texture so generated UI shapes can be smoothly scaled.
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            Color[] clear = new Color[width * height];

            for (int pixelIndex = 0; pixelIndex < clear.Length; pixelIndex++)
            {
                clear[pixelIndex] = Color.clear;
            }

            texture.SetPixels(clear);
            return texture;
        }

        private static void FillRect(Texture2D texture, int pixelX, int pixelY, int width2, int height2, Color colour)
        {
            // Clamps rectangle bounds to the texture before writing pixels, preventing drawing outside the icon.
            int rightEdge = Mathf.Clamp(pixelX + width2, 0, texture.width);
            int topEdge = Mathf.Clamp(pixelY + height2, 0, texture.height);
            pixelX = Mathf.Clamp(pixelX, 0, texture.width);
            pixelY = Mathf.Clamp(pixelY, 0, texture.height);

            for (int pixelY2 = pixelY; pixelY2 < topEdge; pixelY2++)
            {
                for (int pixelX2 = pixelX; pixelX2 < rightEdge; pixelX2++)
                {
                    texture.SetPixel(pixelX2, pixelY2, colour);
                }
            }
        }

        private static void FillPoly(Texture2D texture, Vector2 firstPoint, Vector2 secondPoint, Vector2 thirdPoint, Color colour)
        {
            // Scans only the triangle's bounding box, then fills pixels that pass the point-in-triangle test.
            int minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(firstPoint.x, Mathf.Min(secondPoint.x, thirdPoint.x))), 0, texture.width - 1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(firstPoint.x, Mathf.Max(secondPoint.x, thirdPoint.x))), 0, texture.width - 1);
            int minY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(firstPoint.y, Mathf.Min(secondPoint.y, thirdPoint.y))), 0, texture.height - 1);
            int maxY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(firstPoint.y, Mathf.Max(secondPoint.y, thirdPoint.y))), 0, texture.height - 1);

            for (int pixelY = minY; pixelY <= maxY; pixelY++)
            {
                for (int pixelX = minX; pixelX <= maxX; pixelX++)
                {
                    if (InsideTri(pixelX + 0.5f, pixelY + 0.5f, firstPoint, secondPoint, thirdPoint))
                    {
                        texture.SetPixel(pixelX, pixelY, colour);
                    }
                }
            }
        }

        private static bool InsideTri(float coordinateX, float coordinateY, Vector2 firstPoint, Vector2 secondPoint, Vector2 thirdPoint)
        {
            // Compares signed edge tests; a point is inside when it is never on mixed sides of triangle edges.
            float firstEdgeSign = Sign(coordinateX, coordinateY, firstPoint, secondPoint);
            float secondEdgeSign = Sign(coordinateX, coordinateY, secondPoint, thirdPoint);
            float thirdEdgeSign = Sign(coordinateX, coordinateY, thirdPoint, firstPoint);

            // A mixture of positive and negative edge signs means the point lies outside the triangle.
            bool hasNeg = firstEdgeSign < 0f || secondEdgeSign < 0f || thirdEdgeSign < 0f;
            bool hasPos = firstEdgeSign > 0f || secondEdgeSign > 0f || thirdEdgeSign > 0f;
            return !(hasNeg && hasPos);
        }

        private static float Sign(float coordinateX, float coordinateY, Vector2 edgeStart, Vector2 edgeEnd)
        {
            // Calculates a two-dimensional cross-product sign showing which side of a directed edge a point lies on.
            return (coordinateX - edgeEnd.x) * (edgeStart.y - edgeEnd.y) - (edgeStart.x - edgeEnd.x) * (coordinateY - edgeEnd.y);
        }

        private static void StrokeCircle(Texture2D texture, int centreX, int centreY, int radius, Color colour)
        {
            // Fills only pixels between outer and inner squared radii to create a thick circular outline efficiently.
            int radiusSquared = radius * radius;
            int rInner = (radius - 4) * (radius - 4);
            int minX = Mathf.Max(0, centreX - radius - 1);
            int maxX = Mathf.Min(texture.width - 1, centreX + radius + 1);
            int minY = Mathf.Max(0, centreY - radius - 1);
            int maxY = Mathf.Min(texture.height - 1, centreY + radius + 1);

            for (int pixelY = minY; pixelY <= maxY; pixelY++)
            {
                for (int pixelX = minX; pixelX <= maxX; pixelX++)
                {
                    int horizontalOffset = pixelX - centreX;
                    int verticalOffset = pixelY - centreY;
                    int squaredDistance = horizontalOffset * horizontalOffset + verticalOffset * verticalOffset;

                    // Uses squared distance so the circle test avoids a square root for every pixel.
                    if (squaredDistance <= radiusSquared && squaredDistance >= rInner)
                    {
                        texture.SetPixel(pixelX, pixelY, colour);
                    }
                }
            }
        }

        private static void StrokeArc(Texture2D texture, int centreX, int centreY, int radius, Color colour)
        {
            // Reuses the ring-distance test but scans only the upper half to create a lock shackle arc.
            int radiusSquared = radius * radius;
            int rInner = (radius - 5) * (radius - 5);
            int minX = Mathf.Max(0, centreX - radius - 1);
            int maxX = Mathf.Min(texture.width - 1, centreX + radius + 1);
            int minY = Mathf.Max(0, centreY);
            int maxY = Mathf.Min(texture.height - 1, centreY + radius + 1);

            for (int pixelY = minY; pixelY <= maxY; pixelY++)
            {
                for (int pixelX = minX; pixelX <= maxX; pixelX++)
                {
                    int horizontalOffset = pixelX - centreX;
                    int verticalOffset = pixelY - centreY;
                    int squaredDistance = horizontalOffset * horizontalOffset + verticalOffset * verticalOffset;

                    // Keeps only pixels in the ring thickness to draw the curved lock shackle.
                    if (squaredDistance <= radiusSquared && squaredDistance >= rInner)
                    {
                        texture.SetPixel(pixelX, pixelY, colour);
                    }
                }
            }
        }
    }
}
