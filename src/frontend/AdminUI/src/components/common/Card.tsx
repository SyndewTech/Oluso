import { HTMLAttributes, createContext, useContext } from 'react';
import { clsx } from 'clsx';

type CardPadding = 'none' | 'sm' | 'md' | 'lg';

const CardContext = createContext<{ padding: CardPadding }>({ padding: 'md' });

const paddingValues = {
  none: { card: '', content: '', header: '', footer: '' },
  sm: { card: '', content: 'px-4 py-3', header: 'px-4 py-3', footer: 'px-4 py-3' },
  md: { card: '', content: 'px-6 py-4', header: 'px-6 py-4', footer: 'px-6 py-4' },
  lg: { card: '', content: 'px-8 py-6', header: 'px-8 py-6', footer: 'px-8 py-6' },
};

interface CardProps extends HTMLAttributes<HTMLDivElement> {
  padding?: CardPadding;
}

export function Card({ className, padding = 'md', children, ...props }: CardProps) {
  return (
    <CardContext.Provider value={{ padding }}>
      <div
        className={clsx('bg-white rounded-lg shadow-sm border border-gray-200 overflow-hidden', className)}
        {...props}
      >
        {children}
      </div>
    </CardContext.Provider>
  );
}

interface CardHeaderProps extends Omit<HTMLAttributes<HTMLDivElement>, 'title'> {
  title: React.ReactNode;
  description?: string;
  action?: React.ReactNode;
  icon?: React.ReactNode;
  borderless?: boolean;
}

export function CardHeader({ title, description, action, icon, className, borderless = false, ...props }: CardHeaderProps) {
  const { padding } = useContext(CardContext);

  return (
    <div
      className={clsx(
        'flex items-center justify-between',
        paddingValues[padding].header,
        !borderless && 'border-b border-gray-200',
        className
      )}
      {...props}
    >
      <div className="flex items-start gap-3">
        {icon && <div className="flex-shrink-0 mt-0.5">{icon}</div>}
        <div>
          <h3 className="text-lg font-medium leading-6 text-gray-900">{title}</h3>
          {description && <p className="mt-1 text-sm text-gray-500">{description}</p>}
        </div>
      </div>
      {action && <div className="ml-4 flex-shrink-0">{action}</div>}
    </div>
  );
}

interface CardContentProps extends HTMLAttributes<HTMLDivElement> {}

export function CardContent({ className, children, ...props }: CardContentProps) {
  const { padding } = useContext(CardContext);

  return (
    <div className={clsx(paddingValues[padding].content, className)} {...props}>
      {children}
    </div>
  );
}

interface CardFooterProps extends HTMLAttributes<HTMLDivElement> {
  borderless?: boolean;
}

export function CardFooter({ className, children, borderless = false, ...props }: CardFooterProps) {
  const { padding } = useContext(CardContext);

  return (
    <div
      className={clsx(
        paddingValues[padding].footer,
        !borderless && 'border-t border-gray-200 bg-gray-50',
        className
      )}
      {...props}
    >
      {children}
    </div>
  );
}
