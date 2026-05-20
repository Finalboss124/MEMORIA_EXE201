---
name: Memoria Digital Legacy
colors:
  surface: '#fbf9f1'
  surface-dim: '#dcdad2'
  surface-bright: '#fbf9f1'
  surface-container-lowest: '#ffffff'
  surface-container-low: '#f5f4ec'
  surface-container: '#f0eee6'
  surface-container-high: '#eae8e0'
  surface-container-highest: '#e4e3db'
  on-surface: '#1b1c17'
  on-surface-variant: '#4f4445'
  inverse-surface: '#30312c'
  inverse-on-surface: '#f3f1e9'
  outline: '#807475'
  outline-variant: '#d2c3c4'
  surface-tint: '#70585b'
  primary: '#70585b'
  on-primary: '#ffffff'
  primary-container: '#fadadd'
  on-primary-container: '#765e61'
  inverse-primary: '#debfc2'
  secondary: '#486457'
  on-secondary: '#ffffff'
  secondary-container: '#caead8'
  on-secondary-container: '#4e6a5c'
  tertiary: '#5c5d6e'
  on-tertiary: '#ffffff'
  tertiary-container: '#e0e0f4'
  on-tertiary-container: '#616373'
  error: '#ba1a1a'
  on-error: '#ffffff'
  error-container: '#ffdad6'
  on-error-container: '#93000a'
  primary-fixed: '#fbdbde'
  primary-fixed-dim: '#debfc2'
  on-primary-fixed: '#281719'
  on-primary-fixed-variant: '#574144'
  secondary-fixed: '#caead8'
  secondary-fixed-dim: '#aecebd'
  on-secondary-fixed: '#042016'
  on-secondary-fixed-variant: '#314c3f'
  tertiary-fixed: '#e1e1f5'
  tertiary-fixed-dim: '#c5c5d8'
  on-tertiary-fixed: '#191b29'
  on-tertiary-fixed-variant: '#444655'
  background: '#fbf9f1'
  on-background: '#1b1c17'
  surface-variant: '#e4e3db'
typography:
  headline-xl:
    fontFamily: Plus Jakarta Sans
    fontSize: 48px
    fontWeight: '700'
    lineHeight: 56px
    letterSpacing: -0.02em
  headline-lg:
    fontFamily: Plus Jakarta Sans
    fontSize: 32px
    fontWeight: '700'
    lineHeight: 40px
  headline-md:
    fontFamily: Plus Jakarta Sans
    fontSize: 24px
    fontWeight: '600'
    lineHeight: 32px
  body-lg:
    fontFamily: Plus Jakarta Sans
    fontSize: 18px
    fontWeight: '400'
    lineHeight: 28px
  body-md:
    fontFamily: Plus Jakarta Sans
    fontSize: 16px
    fontWeight: '400'
    lineHeight: 24px
  label-md:
    fontFamily: Plus Jakarta Sans
    fontSize: 14px
    fontWeight: '600'
    lineHeight: 20px
    letterSpacing: 0.01em
  label-sm:
    fontFamily: Plus Jakarta Sans
    fontSize: 12px
    fontWeight: '500'
    lineHeight: 16px
  headline-lg-mobile:
    fontFamily: Plus Jakarta Sans
    fontSize: 28px
    fontWeight: '700'
    lineHeight: 36px
rounded:
  sm: 0.25rem
  DEFAULT: 0.5rem
  md: 0.75rem
  lg: 1rem
  xl: 1.5rem
  full: 9999px
spacing:
  container-max: 1200px
  gutter: 24px
  margin-desktop: 64px
  stack-sm: 8px
  stack-md: 16px
  stack-lg: 32px
  section-gap: 80px
---

## Brand & Style

The design system is centered on the concept of "Digital Legacy"—capturing and preserving emotional milestones for the future. The brand personality is nurturing, nostalgic, and deeply secure, acting as a digital time capsule. 

The visual style is a refined blend of **Minimalism** and **Soft-Tactile design**. It utilizes "marshmallow" geometry—large corner radii and pill-shaped containers—to create a sense of safety and approachability. By combining a warm, cream-based foundation with translucent pastel overlays, the UI evokes an airy, dreamlike atmosphere that feels both modern and emotionally resonant. The interface avoids sharp edges or aggressive transitions, favoring soft gradients and gentle elevation to guide the user through their most precious memories.

## Colors

The palette is a curated selection of "Keepsake Pastels" designed to feel soothing and timeless.

- **Primary (Soft Pink):** Used for primary actions, branding, and highlighting "heart-centered" content.
- **Secondary (Mint Green):** Used for success states, growth-related metrics, and secondary interactive zones.
- **Tertiary (Lavender):** Used for informational badges, scheduled events, and "future-dated" content.
- **Neutral (Cream Canvas):** The base background color, replacing stark white to provide a warmer, more paper-like tactile feel.
- **Surface Overlays:** Containers use white with high transparency (60-80%) or very subtle background blurs to create the glass-like layering seen in the reference.

## Typography

This design system utilizes **Plus Jakarta Sans** for its friendly, rounded terminals and modern geometric structure. 

The type hierarchy is designed to be highly legible with generous line heights to maintain the "airy" feel of the layout. Headlines should use a tighter letter-spacing to feel more cohesive, while labels use a slight positive tracking for clarity at small sizes. All text should be rendered in a deep charcoal brown rather than pure black to maintain the warmth of the palette.

## Layout & Spacing

The layout follows a **Fixed Grid** philosophy for desktop to ensure the content feels contained and safe, like a physical box. 

- **Grid:** 12-column system with a 24px gutter.
- **Margins:** Large external margins (64px+) are essential to emphasize the "clean and airy" aesthetic.
- **Rhythm:** Spacing follows an 8px base unit. Component internal padding should be generous (typically 24px or 32px) to prevent the UI from feeling cramped.
- **Adaptive Strategy:** On desktop, cards are arranged in clusters or wide-span rows. On mobile, elements reflow into a single-column stack with margins reduced to 20px.

## Elevation & Depth

Hierarchy is established through **Tonal Layering** and **Ambient Shadows**.

1.  **Level 0 (Floor):** The Cream (#FFFDF5) base layer.
2.  **Level 1 (Cards):** White or pale pastel containers with a very soft, highly diffused shadow (Blur: 20px, Spread: -5px, Opacity: 6%, Color: Deep Pink/Grey mix).
3.  **Level 2 (Interactive):** Elements like "Quick Action" buttons use a slightly deeper shadow or a subtle inner glow to appear "squishy" and pressable.
4.  **Glassmorphism:** Navigation bars and modal overlays should use a backdrop blur (12px) with a semi-transparent white stroke (1px, 20% opacity) to define edges without adding visual weight.

## Shapes

The shape language is defined by extreme roundedness to reinforce the "friendly and secure" brand pillar.

- **Standard Containers:** Use `rounded-lg` (16px) for most cards.
- **Interactive Elements:** Buttons and Input fields use `rounded-xl` (24px) or full pill shapes to invite interaction.
- **Icon Enclosures:** Small decorative icons or avatars should always be housed in soft-circles or "squircle" containers.
- **Visual Motif:** Subtle, organic "blob" shapes in the background (using 5% opacity pastels) can be used to break the rigidity of the grid.

## Components

- **Buttons:** Primary buttons are pill-shaped, using the accent pink. Secondary buttons use a mint or lavender background with zero border, relying on the soft shadow for definition.
- **Cards:** Cards should have no visible borders. Instead, use color-blocking with the pastel palette to differentiate content types (e.g., Pink cards for "Memories," Mint for "Tokens").
- **Inputs:** Text fields are large with 16px internal padding and a soft cream-to-white gradient background. The focus state is a subtle 2px pastel glow.
- **Chips/Badges:** Small, pill-shaped tags used for status (e.g., "Pending," "Sealed"). These use high-contrast text on a low-saturation version of the tag's color.
- **Progress Bars:** Thick, rounded tracks with a vibrant gradient fill (e.g., Mint to Lavender) to make data visualization feel like a playful element rather than a cold metric.
- **Future Postbox:** A specialized list component featuring circular trailing icons and multi-line descriptive text, separated by generous vertical whitespace rather than dividers.