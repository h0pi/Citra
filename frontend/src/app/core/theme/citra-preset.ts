import { definePreset } from '@primeuix/themes';
import Aura from '@primeuix/themes/aura';

/**
 * Citra's PrimeNG theme: Aura with a "paper and teal ink" look — warm off-white surfaces and one deep teal accent
 * (light), warm charcoal with a lighter teal (dark). Every PrimeNG component (buttons, inputs, tables, ...) picks this
 * up automatically, and the same colors are exposed as CSS variables (--p-primary-600, --p-surface-100, ...).
 */
export const CitraPreset = definePreset(Aura, {
  semantic: {
    primary: {
      50: '#eff7f6',
      100: '#dcefec',
      200: '#bfe0db',
      300: '#8fcac2',
      400: '#5fb3a9',
      500: '#3a8a81',
      600: '#1f5f5b',
      700: '#1a4e4b',
      800: '#163f3d',
      900: '#123331',
      950: '#0a1f1e',
    },
    colorScheme: {
      light: {
        surface: {
          0: '#ffffff',
          50: '#faf8f3',
          100: '#f2efe7',
          200: '#e3dfd3',
          300: '#cfc9b9',
          400: '#a8a293',
          500: '#6f6a5e',
          600: '#5d5a52',
          700: '#4a4740',
          800: '#3a3832',
          900: '#2b2a26',
          950: '#1f1e1b',
        },
        // Buttons and links in the deep teal ink (Aura uses the lighter 500 by default).
        primary: {
          color: '{primary.600}',
          contrastColor: '#ffffff',
          hoverColor: '{primary.700}',
          activeColor: '{primary.800}',
        },
      },
      dark: {
        surface: {
          0: '#f1ede4',
          50: '#e9e5db',
          100: '#d9d5cb',
          200: '#b3ad9f',
          300: '#a39e91',
          400: '#8f8a7e',
          500: '#6e6a60',
          600: '#4f4c45',
          700: '#3a3832',
          800: '#2c2a25',
          900: '#1f1e1b',
          950: '#161512',
        },
      },
    },
  },
});
