import type { MobileTab } from '../types/atrium';
import {
  CalendarIcon,
  ChatIcon,
  GraduationCapIcon,
  NoteIcon,
  StreamIcon,
} from './Icons';

interface MobileTabBarProps {
  activeTab: MobileTab;
  isChatOpen?: boolean;
  onSelectTab: (tab: MobileTab) => void;
  onToggleChat?: () => void;
}

export function MobileTabBar({
  activeTab,
  isChatOpen = false,
  onSelectTab,
  onToggleChat,
}: MobileTabBarProps) {
  const tabs: { id: MobileTab; label: string; icon: typeof StreamIcon }[] = [
    { id: 'stream', label: 'Day', icon: StreamIcon },
    { id: 'calendar', label: 'Calendar', icon: CalendarIcon },
    { id: 'university', label: 'University', icon: GraduationCapIcon },
    { id: 'notes', label: 'Notes', icon: NoteIcon },
  ];

  return (
    <nav className="mobile-tabbar" aria-label="Mobile sections">
      {tabs.map((tab) => {
        const Icon = tab.icon;
        const isActive = !isChatOpen && activeTab === tab.id;
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
            </span>
            <span className="mobile-tab-label">{tab.label}</span>
          </button>
        );
      })}
      {onToggleChat && (
        <button
          id="mobile-tab-assistant"
          type="button"
          className={`mobile-tab-btn ${isChatOpen ? 'is-active' : ''}`}
          aria-pressed={isChatOpen}
          onClick={onToggleChat}
        >
          <span className="mobile-tab-icon-wrap">
            <ChatIcon />
          </span>
          <span className="mobile-tab-label">Assistant</span>
        </button>
      )}
    </nav>
  );
}
