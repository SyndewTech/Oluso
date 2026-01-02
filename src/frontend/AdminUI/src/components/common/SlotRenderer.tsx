import { Suspense } from 'react';
import { useLocation } from 'react-router-dom';
import { usePageSlots, type PageSlot } from '../../contexts/PluginContext';

interface SlotRendererProps {
  /** The position to render slots for */
  position: PageSlot['position'];
  /** Additional context data to pass to slot components */
  context?: Record<string, unknown>;
  /** Custom className for the wrapper */
  className?: string;
  /** Fallback component while loading lazy slots */
  fallback?: React.ReactNode;
}

/**
 * Renders all registered slots for a given position on the current page.
 *
 * Usage in layout:
 * ```tsx
 * <SlotRenderer position="header-actions" />
 * <SlotRenderer position="before-content" context={{ entityId: id }} />
 * ```
 */
export function SlotRenderer({
  position,
  context,
  className,
  fallback = null
}: SlotRendererProps) {
  const location = useLocation();
  const slots = usePageSlots(location.pathname, position);

  if (slots.length === 0) {
    return null;
  }

  return (
    <div className={className}>
      {slots.map((slot) => {
        const SlotComponent = slot.component;
        return (
          <Suspense key={slot.id} fallback={fallback}>
            <SlotComponent
              pagePath={window.location.pathname}
              context={context}
            />
          </Suspense>
        );
      })}
    </div>
  );
}

/**
 * Individual slot positions with semantic wrappers
 */
export function HeaderSlot({ context, className }: Omit<SlotRendererProps, 'position'>) {
  return <SlotRenderer position="header" context={context} className={className} />;
}

export function HeaderActionsSlot({ context, className }: Omit<SlotRendererProps, 'position'>) {
  return <SlotRenderer position="header-actions" context={context} className={className || 'flex items-center gap-2'} />;
}

export function SidebarSlot({ context, className }: Omit<SlotRendererProps, 'position'>) {
  return <SlotRenderer position="sidebar" context={context} className={className} />;
}

export function BeforeContentSlot({ context, className }: Omit<SlotRendererProps, 'position'>) {
  return <SlotRenderer position="before-content" context={context} className={className} />;
}

export function AfterContentSlot({ context, className }: Omit<SlotRendererProps, 'position'>) {
  return <SlotRenderer position="after-content" context={context} className={className} />;
}

export function FooterSlot({ context, className }: Omit<SlotRendererProps, 'position'>) {
  return <SlotRenderer position="footer" context={context} className={className} />;
}

export default SlotRenderer;
