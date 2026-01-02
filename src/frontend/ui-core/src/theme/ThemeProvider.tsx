import { createContext, useContext, useState, useEffect, type ReactNode } from 'react';

/**
 * Theme configuration
 */
export interface ThemeConfig {
  /** Theme name */
  name: string;
  /** Color mode */
  mode: 'light' | 'dark' | 'system';
  /** Primary color (CSS variable value) */
  primaryColor?: string;
  /** Brand colors */
  colors?: {
    primary?: ColorScale;
    secondary?: ColorScale;
    accent?: ColorScale;
  };
  /** Logo configuration */
  logo?: {
    light?: string;
    dark?: string;
    height?: number;
  };
  /** Custom CSS */
  customCss?: string;
}

type ColorScale = {
  50?: string;
  100?: string;
  200?: string;
  300?: string;
  400?: string;
  500?: string;
  600?: string;
  700?: string;
  800?: string;
  900?: string;
  950?: string;
};

interface ThemeContextValue {
  theme: ThemeConfig;
  setTheme: (theme: Partial<ThemeConfig>) => void;
  isDark: boolean;
  toggleMode: () => void;
}

const defaultTheme: ThemeConfig = {
  name: 'default',
  mode: 'light',
};

const ThemeContext = createContext<ThemeContextValue | null>(null);

/**
 * Hook to access theme context
 */
export function useTheme() {
  const context = useContext(ThemeContext);
  if (!context) {
    throw new Error('useTheme must be used within a ThemeProvider');
  }
  return context;
}

interface ThemeProviderProps {
  children: ReactNode;
  /** Initial theme configuration */
  theme?: Partial<ThemeConfig>;
  /** Storage key for persisting theme preference */
  storageKey?: string;
}

/**
 * Provider for theme configuration
 */
export function ThemeProvider({
  children,
  theme: initialTheme,
  storageKey = 'oluso-admin-theme',
}: ThemeProviderProps) {
  const [theme, setThemeState] = useState<ThemeConfig>(() => {
    // Try to load from storage
    if (typeof window !== 'undefined') {
      const stored = localStorage.getItem(storageKey);
      if (stored) {
        try {
          return { ...defaultTheme, ...JSON.parse(stored), ...initialTheme };
        } catch {
          // Invalid stored value
        }
      }
    }
    return { ...defaultTheme, ...initialTheme };
  });

  const [isDark, setIsDark] = useState(false);

  // Determine if dark mode is active
  useEffect(() => {
    const updateDarkMode = () => {
      if (theme.mode === 'dark') {
        setIsDark(true);
      } else if (theme.mode === 'light') {
        setIsDark(false);
      } else {
        // System preference
        setIsDark(window.matchMedia('(prefers-color-scheme: dark)').matches);
      }
    };

    updateDarkMode();

    // Listen for system preference changes
    const mediaQuery = window.matchMedia('(prefers-color-scheme: dark)');
    mediaQuery.addEventListener('change', updateDarkMode);

    return () => mediaQuery.removeEventListener('change', updateDarkMode);
  }, [theme.mode]);

  // Apply dark mode class to document
  useEffect(() => {
    if (isDark) {
      document.documentElement.classList.add('dark');
    } else {
      document.documentElement.classList.remove('dark');
    }
  }, [isDark]);

  // Apply custom CSS variables
  useEffect(() => {
    const root = document.documentElement;

    // Apply color scales
    if (theme.colors?.primary) {
      Object.entries(theme.colors.primary).forEach(([key, value]) => {
        if (value) {
          root.style.setProperty(`--color-primary-${key}`, value);
        }
      });
    }

    if (theme.colors?.secondary) {
      Object.entries(theme.colors.secondary).forEach(([key, value]) => {
        if (value) {
          root.style.setProperty(`--color-secondary-${key}`, value);
        }
      });
    }

    // Apply custom CSS
    let styleElement = document.getElementById('oluso-theme-custom');
    if (theme.customCss) {
      if (!styleElement) {
        styleElement = document.createElement('style');
        styleElement.id = 'oluso-theme-custom';
        document.head.appendChild(styleElement);
      }
      styleElement.textContent = theme.customCss;
    } else if (styleElement) {
      styleElement.remove();
    }

    return () => {
      // Cleanup custom CSS on unmount
      const el = document.getElementById('oluso-theme-custom');
      if (el) el.remove();
    };
  }, [theme.colors, theme.customCss]);

  const setTheme = (updates: Partial<ThemeConfig>) => {
    setThemeState(prev => {
      const next = { ...prev, ...updates };
      // Persist to storage
      if (typeof window !== 'undefined') {
        localStorage.setItem(storageKey, JSON.stringify(next));
      }
      return next;
    });
  };

  const toggleMode = () => {
    setTheme({
      mode: theme.mode === 'light' ? 'dark' : theme.mode === 'dark' ? 'system' : 'light',
    });
  };

  return (
    <ThemeContext.Provider value={{ theme, setTheme, isDark, toggleMode }}>
      {children}
    </ThemeContext.Provider>
  );
}

export default ThemeProvider;
