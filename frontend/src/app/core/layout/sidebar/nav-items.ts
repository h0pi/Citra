export interface NavItem {
  label: string;
  /** PrimeIcons class, e.g. "pi pi-file". */
  icon: string;
  route: string;
}

export interface NavSection {
  /** Optional small heading above the group. */
  heading?: string;
  items: NavItem[];
}

/** Sidebar navigation. Routes must match app.routes.ts. */
export const NAV_SECTIONS: NavSection[] = [
  {
    items: [{ label: 'Dashboard', icon: 'pi pi-chart-bar', route: '/dashboard' }],
  },
  {
    heading: 'Knowledge',
    items: [
      { label: 'Bots', icon: 'pi pi-microchip-ai', route: '/bots' },
      { label: 'Documents', icon: 'pi pi-file', route: '/documents' },
      { label: 'Chat', icon: 'pi pi-comments', route: '/chat' },
    ],
  },
  {
    heading: 'Quality',
    items: [{ label: 'Evaluation', icon: 'pi pi-verified', route: '/evaluation' }],
  },
  {
    heading: 'Account',
    items: [{ label: 'Settings', icon: 'pi pi-cog', route: '/settings' }],
  },
];
