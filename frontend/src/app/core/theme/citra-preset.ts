import { definePreset } from '@primeuix/themes';
import Aura from '@primeuix/themes/aura';

/**
 * Citra's PrimeNG theme: Aura with an indigo brand color.
 * Every PrimeNG component (buttons, inputs, tables, ...) picks this up automatically,
 * and the same colors are exposed as CSS variables (--p-primary-500, --p-surface-100, ...).
 */
export const CitraPreset = definePreset(Aura, {
  semantic: {
    primary: {
      50: '{indigo.50}',
      100: '{indigo.100}',
      200: '{indigo.200}',
      300: '{indigo.300}',
      400: '{indigo.400}',
      500: '{indigo.500}',
      600: '{indigo.600}',
      700: '{indigo.700}',
      800: '{indigo.800}',
      900: '{indigo.900}',
      950: '{indigo.950}',
    },
  },
});
