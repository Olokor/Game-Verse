import { Gamepad2, Home, Play, User, Users, type LucideIcon } from 'lucide-react';

export interface NavItem {
  to: string;
  label: string;
  icon: LucideIcon;
  /** The centre Play action, drawn in volt. */
  primary?: boolean;
}

/** Five items, Play in the centre (DESIGN_SYSTEM.md section 7). */
export const navItems: NavItem[] = [
  { to: '/', label: 'Home', icon: Home },
  { to: '/games', label: 'Games', icon: Gamepad2 },
  { to: '/play', label: 'Play', icon: Play, primary: true },
  { to: '/social', label: 'Social', icon: Users },
  { to: '/profile', label: 'Profile', icon: User },
];
