import { CurrentUser } from '../models/auth.model';

export type BadgeType = 'ai' | 'counter' | 'warning';

export interface MenuBadge {
  type: BadgeType;
  text?: string;
  badgeKey?: 'openJobs' | 'pendingApplications' | 'todayInterviews';
  cssClass?: string;
}

export interface MenuItem {
  label: string;
  route: string;
  icon: string;
  canAccess: (user: CurrentUser | null) => boolean;
  badge?: MenuBadge;
}

export interface MenuGroup {
  groupTitle: string;
  groupKey: 'DESK' | 'RECRUITMENT' | 'ORG_EVAL' | 'SYSTEM';
  items: MenuItem[];
}

/**
 * Helper chuẩn hóa nhận diện 4 Role chính trong hệ thống:
 * 1. ADMIN (Ban Giám Đốc - Chức cao nhất: Full quyền 100% - 12/12 menu)
 * 2. MANAGER (Trưởng Phòng / Quản Đốc Xưởng: Quản lý cấp bộ phận - 5 menu)
 * 3. HR (Chuyên Viên Nhân Sự: Vận hành tuyển dụng - 8 menu)
 * 4. INTERVIEWER (Hội Đồng Phỏng Vấn: Chấm phỏng vấn & Soạn đề - 3 menu)
 */
export function isAdminRole(user: CurrentUser | null): boolean {
  if (!user) return false;
  return user.role === 'ADMIN' || user.role === 'SuperAdmin';
}

export function isManagerRole(user: CurrentUser | null): boolean {
  if (!user) return false;
  return user.role === 'MANAGER' || user.role === 'DepartmentManager';
}

export function isHrRole(user: CurrentUser | null): boolean {
  if (!user) return false;
  return user.role === 'HR' || (user.role === 'Staff' && user.departmentCode === 'HR_TEAM');
}

export function isInterviewerRole(user: CurrentUser | null): boolean {
  if (!user) return false;
  return user.role === 'INTERVIEWER' || (user.role === 'Staff' && user.departmentCode === 'INT_TEAM');
}

/**
 * ════════════════════════════════════════════════════════════════════════════
 * CẤU HÌNH MENU PHÂN NHÓM DOANH NGHIỆP (GROUPED NAVIGATION)
 * Hệ thống Quản lý Tuyển dụng Nhân sự (ATS) Nhà máy / KCN
 * ════════════════════════════════════════════════════════════════════════════
 */
export const SIDEBAR_MENU_GROUPS: MenuGroup[] = [
  // ─── NHÓM 1 - BÀN LÀM VIỆC ──────────────────────────────────────────────
  {
    groupTitle: 'BÀN LÀM VIỆC',
    groupKey: 'DESK',
    items: [
      {
        label: 'Tổng Quan',
        route: '/hr/dashboard',
        icon: 'dashboard',
        canAccess: (user) => isAdminRole(user) || isManagerRole(user) || isHrRole(user) || isInterviewerRole(user)
      },
      {
        label: 'Sàng Lọc Hồ Sơ',
        route: '/hr/ai-screening',
        icon: 'fact_check',
        canAccess: (user) => isAdminRole(user) || isHrRole(user),
        badge: {
          type: 'ai',
          text: 'Tự động',
          cssClass: 'bg-slate-100 text-slate-700 dark:bg-slate-800 dark:text-slate-300 font-semibold text-[10px] px-2 py-0.5 rounded-md border border-slate-200 dark:border-slate-700'
        }
      }
    ]
  },

  // ─── NHÓM 2 - NGHIỆP VỤ TUYỂN DỤNG ─────────────────────────────────────
  {
    groupTitle: 'NGHIỆP VỤ TUYỂN DỤNG',
    groupKey: 'RECRUITMENT',
    items: [
      {
        label: 'Chiến Dịch Việc Làm',
        route: '/hr/jobs',
        icon: 'work',
        canAccess: (user) => isAdminRole(user) || isHrRole(user) || isManagerRole(user),
        badge: {
          type: 'counter',
          badgeKey: 'openJobs',
          cssClass: 'bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-300 font-bold text-xs px-2 py-0.5 rounded-full'
        }
      },
      {
        label: 'Hồ Sơ Ứng Tuyển',
        route: '/hr/applications',
        icon: 'assignment_ind',
        canAccess: (user) => isAdminRole(user) || isHrRole(user),
        badge: {
          type: 'counter',
          badgeKey: 'pendingApplications',
          cssClass: 'bg-red-100 text-red-600 dark:bg-red-900/40 dark:text-red-300 font-bold text-xs px-2 py-0.5 rounded-full ring-1 ring-red-200 dark:ring-red-800'
        }
      },
      {
        label: 'Lịch Phỏng Vấn',
        route: '/hr/interviews',
        icon: 'calendar_month',
        canAccess: (user) => isAdminRole(user) || isHrRole(user) || isManagerRole(user) || isInterviewerRole(user),
        badge: {
          type: 'counter',
          badgeKey: 'todayInterviews',
          cssClass: 'bg-amber-100 text-amber-800 dark:bg-amber-900/40 dark:text-amber-300 font-bold text-xs px-2 py-0.5 rounded-full'
        }
      },
      {
        label: 'Tiếp Nhận & Onboarding',
        route: '/hr/onboarding',
        icon: 'how_to_reg',
        canAccess: (user) => isAdminRole(user) || isHrRole(user)
      }
      // {
      //   label: 'Kho Nhân Tài (Talent Pool)',
      //   route: '/hr/talent-pool',
      //   icon: 'folder_shared',
      //   canAccess: (user) => isAdminRole(user) || isHrRole(user)
      // }
    ]
  },

  // ─── NHÓM 3 - TỔ CHỨC & ĐÁNH GIÁ ────────────────────────────────────────
  {
    groupTitle: 'TỔ CHỨC & ĐÁNH GIÁ',
    groupKey: 'ORG_EVAL',
    items: [
      {
        label: 'Cơ Cấu Nhân Sự',
        route: '/hr/employees',
        icon: 'diversity_3',
        canAccess: (user) => isAdminRole(user) || isManagerRole(user)
      },
      {
        label: 'Tiêu Chuẩn Kỹ Năng',
        route: '/hr/skills',
        icon: 'psychology',
        canAccess: (user) => isAdminRole(user) || isHrRole(user)
      },
      {
        label: 'Ngân Hàng Câu Hỏi',
        route: '/hr/question-bank',
        icon: 'quiz',
        canAccess: (user) => isAdminRole(user) || isInterviewerRole(user)
      },
      {
        label: 'Báo Cáo & SLA',
        route: '/hr/reports',
        icon: 'analytics',
        canAccess: (user) => isAdminRole(user) || isManagerRole(user)
      }
    ]
  },

  // ─── NHÓM 4 - HỆ THỐNG ──────────────────────────────────────────────────
  {
    groupTitle: 'HỆ THỐNG',
    groupKey: 'SYSTEM',
    items: [
      {
        label: 'Huấn Luyện Chatbot FAQ',
        route: '/hr/chatbot-faq',
        icon: 'smart_toy',
        canAccess: (user) => isAdminRole(user) || isHrRole(user)
      },
      {
        label: 'Cài Đặt Hệ Thống',
        route: '/hr/settings',
        icon: 'settings',
        canAccess: (user) => isAdminRole(user)
      }
    ]
  }
];

/**
 * Danh sách phẳng tất cả các Menu Items phục vụ Route Guards và xử lý tương thích
 */
export const SIDEBAR_MENU_ITEMS: MenuItem[] = SIDEBAR_MENU_GROUPS.flatMap(group => group.items);

/**
 * 4 Tài khoản mẫu cho bộ chuyển đổi Role nhanh (Dev Role Switcher)
 */
export const DEV_ACCOUNTS: CurrentUser[] = [
  {
    id: '30000000-0000-0000-0000-000000000001',
    fullName: 'Nguyễn Văn Giám Đốc',
    email: 'giamdoc@factory.com',
    role: 'ADMIN',
    roleDisplayName: 'Ban Giám Đốc',
    departmentId: '10000000-0000-0000-0000-000000000001',
    departmentCode: 'ADMIN_SYS',
    departmentName: 'Ban Giám Đốc'
  },
  {
    id: '30000000-0000-0000-0000-000000000011',
    fullName: 'Phạm Văn Nhân Sự 1',
    email: 'hr1@factory.com',
    role: 'HR',
    roleDisplayName: 'Chuyên Viên Tuyển Dụng',
    departmentId: '10000000-0000-0000-0000-000000000002',
    departmentCode: 'HR_TEAM',
    departmentName: 'Phòng Nhân Sự'
  },
  {
    id: '30000000-0000-0000-0000-000000000030',
    fullName: 'Trịnh Văn Quản Đốc Xưởng SMT',
    email: 'truong.sx@factory.com',
    role: 'MANAGER',
    roleDisplayName: 'Trưởng Phòng / Quản Đốc',
    departmentId: '10000000-0000-0000-0000-000000000004',
    departmentCode: 'PROD_LINE',
    departmentName: 'Xưởng Sản Xuất & Lắp Ráp SMT'
  },
  {
    id: '30000000-0000-0000-0000-000000000021',
    fullName: 'Vũ Thị Phỏng Vấn Viên 1',
    email: 'pv1@factory.com',
    role: 'INTERVIEWER',
    roleDisplayName: 'Phỏng Vấn Viên Chuyên Môn',
    departmentId: '10000000-0000-0000-0000-000000000003',
    departmentCode: 'INT_TEAM',
    departmentName: 'Hội Đồng Phỏng Vấn'
  }
];
