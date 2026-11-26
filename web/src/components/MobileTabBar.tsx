import type { MobileTab } from '../types/atrium';
import {
  AgentIcon,
  CalendarIcon,
  GraduationCapIcon,
  StreamIcon,
  VaultIcon,
} from './Icons';

interface MobileTabBarProps {
  activeTab: MobileTab;
  pendingProposalCount: number;
  onSelectTab: (tab: MobileTab) => void;
}

export function MobileTabBar({
  activeTab,
  pendingProposalCount,
  onSelectTab,
}: MobileTabBarProps) {
  const tabs: { id: MobileTab; label: string; icon: typeof StreamIcon; badge?: number }[] = [
    { id: 'stream', label: 'Stream', icon: StreamIcon },
    { id: 'calendar', label: 'Calendar', icon: CalendarIcon },
    { id: 'university', label: 'University', icon: GraduationCapIcon },
    { id: 'vault', label: 'Vault', icon: VaultIcon },
    {
      id: 'agents',
      label: 'Agents',
      icon: AgentIcon,
      badge: pendingProposalCount > 0 ? pendingProposalCount : undefined,
    },
  ];

  return (
    <nav className="mobile-tabbar" aria-label="Mobile sections">
      {tabs.map((tab) => {
        const Icon = tab.icon;
        const isActive = activeTab === tab.id;
        return (
          <button
            key={tab.id}
            id={`mobile-tab-${tab.id}`}
            type="button"
            className={`mobile-tab-btn ${isActive ? 'is-active' : ''}`}
            aria-current={isActive ? 'page' : undefined}
            onClick={() => onSelectTab(tab.id)}
          >
            <span className="mobile-tab-icon-wrap">
              <Icon />
              {tab.badge !== undefined && (
                <span className="mobile-tab-badge mono-num" aria-hidden="true">
                  {tab.badge}
                </span>
              )}
            </span>
            <span className="mobile-tab-label">{tab.label}</span>
          </button>
        );
      })}
    </nav>
  );
}
