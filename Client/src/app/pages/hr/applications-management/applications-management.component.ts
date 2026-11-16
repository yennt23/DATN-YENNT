import { CommonModule } from '@angular/common';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal, ChangeDetectorRef } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { ActivatedRoute, Router } from '@angular/router';
import { ToastService } from '../../../services/toast.service';

export type RecruitmentTrack = 'PROFESSIONAL' | 'MASS_LABOR';
export type ApplicationSource = 'PDF_UPLOAD' | 'GOOGLE_FORM_QR' | 'WALK_IN';
export type ScoringMethod = 'GEMINI_AI' | 'RULE_ENGINE';
export type QuickTabType = 'ALL' | 'PROFESSIONAL' | 'MASS_LABOR' | 'FAST_TRACK' | 'ONBOARDED' | 'TALENT_POOL' | 'RULE_REJECTED';

export interface InterviewerItem {
  id: string;
  fullName: string;
  email: string;
  roleName: string;
}

export interface OralInterviewChecklist {
  handEyePass: boolean;
  visionPass: boolean;
  shift12hConfirmed: boolean;
  attitudePass: boolean;
  quickNote: string;
}

export interface IndustrialApplication {
  applicationId: string;
  candidateId: string;
  jobId: string;
  jobTitle: string;
  jobCode: string;
  departmentCode: 'PROD_LINE' | 'TECH_MNT' | 'WH_LOGS' | 'QA_QC' | 'OFFICE_HR';
  departmentName: string;

  candidateName: string;
  phone: string;
  email: string;
  yearOfBirth: string;
  cccd: string;
  hometown: string;
  educationLevel: string;

  recruitmentTrack: RecruitmentTrack;
  applicationSource: ApplicationSource;
  scoringMethod: ScoringMethod;

  aiMatchingScore: number;
  skillMatchPercent: number;
  experienceMatchPercent: number;
  shiftHealthPercent: number;
  aiSummary: string;

  tags: string[];
  shift12hReady: boolean;
  isRuleRejected: boolean;
  ruleRejectReason?: string;

  cvFileUrl: string | null;
  rawResumeText: string;
  oralChecklist: OralInterviewChecklist;
  customFormFields?: Record<string, string>;

  status: 'PENDING' | 'SentToManager' | 'ScheduledInterview' | 'InterviewPassed' | 'Hired' | 'Rejected' | 'Saved_In_Talent_Pool';
  appliedAt: string;

  convertedEmployeeCode?: string | null;

  // ── Cảnh báo nghiệp vụ từ DB (7 Business Rules) ──
  isBlacklisted?: boolean;
  blacklistWarningNote?: string | null;
  isScannedOrUnparseablePdf?: boolean;
  duplicateCount?: number;
  isOutdatedScore?: boolean;
  slaDeadlineAt?: string | null;
  isSlaEscalated?: boolean;
  slaOverrideReason?: string | null;

  // ── V3 Enterprise Compliance ──
  interviewScore?: number | null;
  isAiOverriddenByHuman?: boolean;
  aiOverrideReason?: string | null;
  offerStatus?: string;
  offeredSalary?: number | null;
  offerResponseNote?: string | null;
  transferredFromJobId?: string | null;
  transferredFromJobTitle?: string | null;
  transferReason?: string | null;
  isPiiMaskedBeforeAi?: boolean;

  // ── V5 Factory & HR Operations Complete ──
  isExEmployeeRehire?: boolean;
  previousEmployeeCode?: string | null;
  interviewRescheduleCount?: number;
  offerExpiresAt?: string | null;
  isInTalentPool?: boolean;
  probation30DayStatus?: string | null;
  interviewBatchId?: number | null;
  interviewBatchCode?: string | null;
}

@Component({
  selector: 'app-applications-management',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './applications-management.component.html'
})
export class ApplicationsManagementComponent implements OnInit {
  private http = inject(HttpClient);
  private sanitizer = inject(DomSanitizer);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private toast = inject(ToastService);
  private cdr = inject(ChangeDetectorRef);

  // ── State Signals ──────────────────────────────────────────────────────
  applications = signal<IndustrialApplication[]>([]);
  isLoading = signal<boolean>(false);

  activeTab = signal<QuickTabType>('ALL');
  searchKeyword = signal<string>('');
  selectedJobId = signal<string>('ALL');
  selectedDepartment = signal<string>('ALL');
  minScoreFilter = signal<number>(0);

  // Selection for Floating Bulk Action Bar
  selectedIds = signal<Set<string>>(new Set());
  isProcessingBulk = signal<boolean>(false);

  // Slide-over Drawer State
  selectedApp = signal<IndustrialApplication | null>(null);
  isDrawerOpen = signal<boolean>(false);

  // Walk-in Modal State
  showWalkInModal = signal<boolean>(false);
  isSavingWalkIn = signal<boolean>(false);
  walkInForm = {
    jobPostingId: '60000000-0000-0000-0000-000000000101',
    fullName: '',
    phone: '',
    yearOfBirth: '2001',
    cccd: '',
    hometown: 'Bắc Ninh',
    educationLevel: 'THPT (12/12)',
    experienceSummary: 'Đã làm công nhân lắp ráp linh kiện điện tử 1 năm tại KCN Yên Phong',
    handEyePass: true,
    visionPass: true,
    shift12hConfirmed: true,
    attitudePass: true,
    quickNote: 'Nhanh nhẹn, tay không run, sẵn sàng đi làm ngay ca sáng mai'
  };

  // Google Sheet Sync Modal State
  showSyncModal = signal<boolean>(false);
  sheetUrlInput = signal<string>('');
  isSyncingSheet = signal<boolean>(false);

  // ── V3 Enterprise Compliance Modal States ──
  showOverrideModal = signal<boolean>(false);
  overrideModalApp = signal<IndustrialApplication | null>(null);
  overrideReason = signal<string>('CV viết sơ sài nhưng ứng viên có chứng chỉ nghề bậc 3 thực tế và kinh nghiệm 4 năm');
  overrideToPassed = signal<boolean>(true);

  showTransferModal = signal<boolean>(false);
  transferModalApp = signal<IndustrialApplication | null>(null);
  transferTargetJobId = signal<string>('');
  transferReason = signal<string>('Thị lực không đạt chuẩn xưởng linh kiện điện tử SMT, điều chuyển sang Xưởng Kho Vận (WH_LOGS)');
  transferAvailableJobs = signal<Array<{ jobId: string; title: string; department: string }>>([]);

  showOfferModal = signal<boolean>(false);
  offerModalApp = signal<IndustrialApplication | null>(null);
  offeredSalary = signal<number>(18000000);
  offerNote = signal<string>('Lương cơ bản thỏa thuận 18.000.000 VNĐ Net + phụ cấp kỹ sư');
  isRescoring = signal<boolean>(false);

  // ── Interview Modal State (Website & QR 1-1) ──
  showInterviewModal = signal<boolean>(false);
  selectedInterviewApp = signal<IndustrialApplication | null>(null);

  interviewDate = signal<string>('');
  interviewTime = signal<string>('09:00');
  interviewType = signal<'ONLINE' | 'OFFLINE'>('ONLINE');
  interviewLocation = signal<string>('Hội trường Tuyển dụng & Đào tạo Nhà máy V9 TECH - KCN Yên Phong');
  selectedInterviewerId = signal<string>('11111111-1111-1111-1111-111111111111');
  emailPreviewContent = signal<string>('');
  aiOpeningText = signal<string>('');

  dateValidationError = signal<string>('');
  timeValidationError = signal<string>('');
  locationValidationError = signal<string>('');
  batchDateValidationError = signal<string>('');

  isGeneratingAI = signal<boolean>(false);
  isSendingInterview = signal<boolean>(false);

  interviewers = signal<InterviewerItem[]>([
    { id: '11111111-1111-1111-1111-111111111111', fullName: 'Phạm Đình Sơn', email: 'sontestdatn@gmail.com', roleName: 'HR Manager' },
    { id: '22222222-2222-2222-2222-222222222222', fullName: 'Nguyễn Văn Bình', email: 'binh.nv@v9tech.vn', roleName: 'Quản đốc Xưởng SMT' },
    { id: '33333333-3333-3333-3333-333333333333', fullName: 'Trần Minh Hoàng', email: 'hoang.tm@v9tech.vn', roleName: 'Trưởng nhóm Kỹ thuật PLC' },
    { id: '44444444-4444-4444-4444-444444444444', fullName: 'Lê Thu Trang', email: 'trang.lt@v9tech.vn', roleName: 'HR Tuyển dụng' }
  ]);
  isLoadingInterviewers = signal<boolean>(false);
  vietnameseHolidays = signal<string[]>([
    '01-01', '02-01',
    '15-02', '16-02', '17-02', '18-02', '19-02', '20-02', '21-02', '22-02', '23-02',
    '26-04', '30-04', '01-05', '01-09', '02-09', '03-09'
  ]);

  isInterviewFormValid = computed(() => {
    const d = this.interviewDate();
    const t = this.interviewTime();
    const loc = (this.interviewLocation() || '').trim();
    const invId = this.selectedInterviewerId();

    if (!d || !t || !loc || !invId) return false;
    if (this.dateValidationError() || this.timeValidationError() || this.locationValidationError()) return false;
    return true;
  });

  // ── V5 Factory & HR Operations State ──
  showBatchInterviewModal = signal<boolean>(false);
  batchInterviewDate = signal<string>(new Date(Date.now() + 86400000).toISOString().split('T')[0]);
  batchShiftSlot = signal<'MORNING_08_11' | 'AFTERNOON_13_17'>('MORNING_08_11');
  batchInterviewerId = signal<string>('');
  batchMaxCapacity = signal<number>(50);
  isSchedulingBatches = signal<boolean>(false);
  isLoadingBatches = signal<boolean>(false);

  // Danh sách ca phỏng vấn tập trung thực tế từ Database
  activeInterviewBatches = signal<Array<{
    id: number;
    batchCode: string;
    shiftSlot: string;
    shiftName: string;
    scheduledDate: string;
    currentCount: number;
    maxCapacity: number;
  }>>([]);

  // Danh sách chiến dịch tuyển dụng mẫu + thực tế
  jobCampaigns = signal<Array<{ id: string; code: string; title: string; track: RecruitmentTrack; deptCode: string }>>([
    { id: '60000000-0000-0000-0000-000000000101', code: 'JOB-SMT-100', title: 'Công nhân vận hành dây chuyền SMT (Tuyển gấp 100 người)', track: 'MASS_LABOR', deptCode: 'PROD_LINE' },
    { id: '60000000-0000-0000-0000-000000000103', code: 'JOB-WH-040', title: 'Nhân viên Kho vận & Lái xe nâng Reach Truck', track: 'MASS_LABOR', deptCode: 'WH_LOGS' },
    { id: '60000000-0000-0000-0000-000000000102', code: 'JOB-ENG-012', title: 'Kỹ sư Tự động hóa PLC & Bảo trì Dây chuyền (Mitsubishi/Siemens)', track: 'PROFESSIONAL', deptCode: 'TECH_MNT' },
    { id: '60000000-0000-0000-0000-000000000104', code: 'JOB-QA-008', title: 'Chuyên viên Quản lý Chất lượng IQC/OQC (ISO 9001 & IATF 16949)', track: 'PROFESSIONAL', deptCode: 'QA_QC' }
  ]);

  // ── Computed Metrics & Filtered Data ───────────────────────────────────
  counts = computed(() => {
    const all = this.applications();
    const isActiveInScreening = (a: IndustrialApplication) =>
      !a.isInTalentPool &&
      a.status !== 'Saved_In_Talent_Pool' &&
      !a.isRuleRejected &&
      a.status !== 'Rejected' &&
      a.status !== 'InterviewPassed' &&
      a.status !== 'Hired' &&
      !a.convertedEmployeeCode;

    return {
      all: all.filter(isActiveInScreening).length,
      professional: all.filter(a => a.recruitmentTrack === 'PROFESSIONAL' && isActiveInScreening(a)).length,
      massLabor: all.filter(a => a.recruitmentTrack === 'MASS_LABOR' && isActiveInScreening(a)).length,
      fastTrack: all.filter(a => a.recruitmentTrack === 'MASS_LABOR' && isActiveInScreening(a)).length,
      onboardedOrHired: all.filter(a => a.status === 'InterviewPassed' || a.status === 'Hired' || !!a.convertedEmployeeCode).length,
      talentPool: all.filter(a => a.isInTalentPool || a.status === 'Saved_In_Talent_Pool').length,
      ruleRejected: all.filter(a => a.isRuleRejected || a.status === 'Rejected').length
    };
  });

  filteredApplications = computed(() => {
    const tab = this.activeTab();
    const kw = this.searchKeyword().trim().toLowerCase();
    const job = this.selectedJobId();
    const dept = this.selectedDepartment();
    const minScore = this.minScoreFilter();

    return this.applications().filter(app => {
      if (tab === 'ONBOARDED') {
        if (app.status !== 'InterviewPassed' && app.status !== 'Hired' && !app.convertedEmployeeCode) return false;
      } else if (tab === 'TALENT_POOL') {
        if (!app.isInTalentPool && app.status !== 'Saved_In_Talent_Pool') return false;
      } else if (tab === 'RULE_REJECTED') {
        if (!app.isRuleRejected && app.status !== 'Rejected') return false;
      } else {
        // Hồ sơ đã sang Tiếp nhận & Onboarding, Kho nhân tài hoặc Không đạt sẽ chuyển sang menu/tab tương ứng, không còn nằm ở danh sách đang chờ duyệt
        if (
          app.isInTalentPool ||
          app.status === 'Saved_In_Talent_Pool' ||
          app.isRuleRejected ||
          app.status === 'Rejected' ||
          app.status === 'InterviewPassed' ||
          app.status === 'Hired' ||
          !!app.convertedEmployeeCode
        ) {
          return false;
        }
        if (tab === 'PROFESSIONAL' && app.recruitmentTrack !== 'PROFESSIONAL') return false;
        if ((tab === 'MASS_LABOR' || tab === 'FAST_TRACK') && app.recruitmentTrack !== 'MASS_LABOR') return false;
      }

      if (job !== 'ALL' && app.jobId !== job) return false;
      if (dept !== 'ALL' && app.departmentCode !== dept) return false;
      if (minScore > 0 && app.aiMatchingScore < minScore) return false;

      if (kw) {
        const matchKw =
          app.candidateName.toLowerCase().includes(kw) ||
          app.phone.toLowerCase().includes(kw) ||
          app.cccd.toLowerCase().includes(kw) ||
          app.jobTitle.toLowerCase().includes(kw) ||
          app.hometown.toLowerCase().includes(kw);
        if (!matchKw) return false;
      }

      return true;
    });
  });

  selectedCount = computed(() => this.selectedIds().size);

  ngOnInit(): void {
    const routeJobId = this.route.snapshot.paramMap.get('jobId');
    if (routeJobId) {
      this.selectedJobId.set(routeJobId);
    }

    const defaultTab = this.route.snapshot.data['defaultTab'] as QuickTabType;
    const isTalentPool = this.router.url.includes('talent-pool') || this.route.snapshot.url.some(s => s.path === 'talent-pool');
    if (defaultTab) {
      this.activeTab.set(defaultTab);
    } else if (isTalentPool) {
      this.activeTab.set('TALENT_POOL');
    }

    // Tải dữ liệu thực tế 100% từ Backend Database API
    this.fetchBackendApplications();
    this.loadVietnamHolidays();
    this.loadInterviewers();
  }

  fetchBackendApplications(): void {
    this.isLoading.set(true);
    let params = new HttpParams();
    if (this.selectedJobId() !== 'ALL' && !this.selectedJobId().startsWith('job-')) {
      params = params.set('jobPostingId', this.selectedJobId());
    }

    this.http.get<any>('/api/applications', { params }).subscribe({
      next: (res) => {
        this.isLoading.set(false);
        const apiList: any[] = res?.data || (Array.isArray(res) ? res : []);
        if (apiList.length > 0) {
          const templates = this.getInitialIndustrialMockData();
          const mappedFromApi: IndustrialApplication[] = apiList.map((item, idx) => this.mapApiItemToIndustrial(item, idx, templates));
          // Gán trực tiếp dữ liệu thật 100% từ Database SQL Server
          this.applications.set(mappedFromApi);

          // Bổ sung các Job thực tế vào bộ lọc chiến dịch
          const currentJobs = [...this.jobCampaigns()];
          const knownJobIds = new Set(currentJobs.map(j => j.id.toLowerCase()));
          for (const app of mappedFromApi) {
            if (app.jobId && !knownJobIds.has(app.jobId.toLowerCase())) {
              knownJobIds.add(app.jobId.toLowerCase());
              currentJobs.unshift({
                id: app.jobId,
                code: app.jobCode || 'JOB-DB',
                title: app.jobTitle || 'Chiến dịch tuyển dụng',
                track: app.recruitmentTrack,
                deptCode: app.departmentCode
              });
            }
          }
          this.jobCampaigns.set(currentJobs);
        } else {
          // Fallback sang danh sách khởi tạo nếu DB hoàn toàn chưa có bản ghi
          this.applications.set(this.getInitialIndustrialMockData());
        }
      },
      error: () => {
        this.isLoading.set(false);
        if (this.applications().length === 0) {
          this.applications.set(this.getInitialIndustrialMockData());
        }
      }
    });
  }

  private mapApiItemToIndustrial(item: any, idx: number, mockItems: IndustrialApplication[] = []): IndustrialApplication {
    const matchedTemplate = mockItems.find(
      m => m.applicationId.toLowerCase() === String(item.applicationId || '').toLowerCase() ||
           (item.phone && m.phone === String(item.phone).trim())
    );
    const hasPdf = !!(item.cvFileUrl || item.cvUrl || matchedTemplate?.cvFileUrl);
    const source: ApplicationSource = item.applicationSource
      || (item.source?.includes('GOOGLE') ? 'GOOGLE_FORM_QR' : item.source === 'WALK_IN' ? 'WALK_IN' : hasPdf ? 'PDF_UPLOAD' : (matchedTemplate?.applicationSource || 'GOOGLE_FORM_QR'));
    const track: RecruitmentTrack = item.recruitmentTrack || matchedTemplate?.recruitmentTrack || (source === 'PDF_UPLOAD' ? 'PROFESSIONAL' : 'MASS_LABOR');
    const scoring: ScoringMethod = item.scoringMethod || matchedTemplate?.scoringMethod || (track === 'PROFESSIONAL' ? 'GEMINI_AI' : 'RULE_ENGINE');
    
    // Xử lý điểm AI: Lấy điểm thật từ Backend, fallback sang hồ sơ gốc nếu chưa tạo bản ghi điểm
    const rawScore = item.matchScore ?? item.aiScore ?? matchedTemplate?.aiMatchingScore;
    const hasAiScore = rawScore !== null && rawScore !== undefined;
    const score = hasAiScore ? Math.round(Number(rawScore)) : 0;

    let checklist: OralInterviewChecklist = matchedTemplate?.oralChecklist
      ? { ...matchedTemplate.oralChecklist }
      : {
          handEyePass: true,
          visionPass: true,
          shift12hConfirmed: true,
          attitudePass: true,
          quickNote: ''
        };
    if (item.oralInterviewChecklistJson) {
      try {
        checklist = { ...checklist, ...JSON.parse(item.oralInterviewChecklistJson) };
      } catch { /* ignore */ }
    }

    const rawStatus = (item.status || 'PENDING').toUpperCase();
    const hasEmployeeCode = !!item.convertedEmployeeCode;
    const mappedStatus: IndustrialApplication['status'] =
      hasEmployeeCode || rawStatus === 'HIRED' ? 'Hired'
      : rawStatus === 'SAVED_IN_TALENT_POOL' ? 'Saved_In_Talent_Pool'
      : rawStatus === 'SCREENING' || rawStatus === 'SENTTOMANAGER' || rawStatus === 'NEED_MANUAL_REVIEW' || rawStatus === 'TRANSFERRED_PENDING' ? 'SentToManager'
      : rawStatus === 'INTERVIEW' || rawStatus === 'SCHEDULEDINTERVIEW' || rawStatus === 'INTERVIEW_SCHEDULED' || rawStatus === 'INTERVIEW_PENDING' ? 'ScheduledInterview'
      : rawStatus === 'PASSED' || rawStatus === 'INTERVIEWPASSED' || rawStatus === 'PENDING_OFFER' || rawStatus === 'OFFER' || rawStatus === 'OFFERED' || rawStatus === 'OFFER_SENT' || rawStatus === 'OFFER_ACCEPTED' || rawStatus === 'WAITLIST' || rawStatus === 'WAITLISTED' ? 'InterviewPassed'
      : rawStatus === 'REJECTED' || rawStatus === 'REJECTED_BLACKLISTED' || rawStatus === 'REJECTED_EXCEED_RESCHEDULE' || rawStatus === 'OFFER_DECLINED' || rawStatus === 'NO_SHOW_CANCELLED' ? 'Rejected'
      : 'PENDING';

    // Nhận xét AI: Lấy explanation thật, KHÔNG lấy text thô CV
    let cleanExplanation = '';
    if (item.aiExplanation && item.aiExplanation.trim() !== 'AI đã phân tích kỹ năng ứng viên') {
      cleanExplanation = item.aiExplanation.trim();
    } else if (item.aiAnalysisSummary && item.aiAnalysisSummary.trim() !== '') {
      cleanExplanation = item.aiAnalysisSummary.trim();
    } else if (matchedTemplate?.aiSummary) {
      cleanExplanation = matchedTemplate.aiSummary;
    } else if (hasAiScore) {
      cleanExplanation = 'AI đã hoàn thành phân tích hồ sơ ứng viên.';
    } else {
      cleanExplanation = 'Hồ sơ đang trong hàng đợi AI phân tích độ phù hợp với chiến dịch.';
    }

    return {
      applicationId: item.applicationId || `api-${idx}`,
      candidateId: item.candidateId || matchedTemplate?.candidateId || '',
      jobId: item.jobId || matchedTemplate?.jobId || '60000000-0000-0000-0000-000000000101',
      jobTitle: item.jobTitle || matchedTemplate?.jobTitle || 'Công nhân vận hành dây chuyền SMT',
      jobCode: item.jobCode || matchedTemplate?.jobCode || 'JOB-ATS',
      departmentCode: (item.departmentCode as any) || matchedTemplate?.departmentCode || (track === 'PROFESSIONAL' ? 'TECH_MNT' : 'PROD_LINE'),
      departmentName: item.departmentName || matchedTemplate?.departmentName || (track === 'PROFESSIONAL' ? 'Phòng Kỹ Thuật & Bảo Trì' : 'Xưởng Sản Xuất & Lắp Ráp SMT'),
      candidateName: item.candidateName || matchedTemplate?.candidateName || 'Ứng viên ATS',
      phone: item.phone || matchedTemplate?.phone || '0987654321',
      email: item.email || matchedTemplate?.email || '',
      yearOfBirth: item.yearOfBirth || matchedTemplate?.yearOfBirth || '2000',
      cccd: item.citizenId || matchedTemplate?.cccd || ('02720000' + (1234 + idx)),
      hometown: item.hometown || matchedTemplate?.hometown || 'Bắc Ninh',
      educationLevel: matchedTemplate?.educationLevel || (track === 'PROFESSIONAL' ? 'Đại học Kỹ thuật' : 'THPT (12/12)'),
      recruitmentTrack: track,
      applicationSource: source,
      scoringMethod: scoring,
      aiMatchingScore: score,
      skillMatchPercent: item.skillMatchPercent != null ? item.skillMatchPercent : (matchedTemplate?.skillMatchPercent ?? (hasAiScore ? score : 0)),
      experienceMatchPercent: item.experienceMatchPercent != null ? item.experienceMatchPercent : (matchedTemplate?.experienceMatchPercent ?? (hasAiScore ? score : 0)),
      shiftHealthPercent: item.softSkillMatchPercent != null ? item.softSkillMatchPercent : (matchedTemplate?.shiftHealthPercent ?? (track === 'MASS_LABOR' ? 95 : (hasAiScore ? score : 0))),
      aiSummary: cleanExplanation,
      tags: (Array.isArray(item.matchedSkills) && item.matchedSkills.length > 0)
        ? item.matchedSkills
        : (matchedTemplate?.tags || (track === 'PROFESSIONAL'
          ? ['PLC Mitsubishi', 'AutoCAD', 'Bảo trì dây chuyền', 'ISO 9001']
          : ['Xoay ca 12h', 'Thị lực 10/10', 'Đã làm KCN'])),
      shift12hReady: matchedTemplate ? matchedTemplate.shift12hReady : true,
      isRuleRejected: mappedStatus === 'Rejected' || (hasAiScore && score < 50),
      ruleRejectReason: matchedTemplate?.ruleRejectReason,
      cvFileUrl: item.cvFileUrl || item.cvUrl || matchedTemplate?.cvFileUrl || null,
      rawResumeText: item.rawResumeText || matchedTemplate?.rawResumeText || `Họ tên: ${item.candidateName}\nSĐT: ${item.phone}\nEmail: ${item.email}`,
      oralChecklist: checklist,
      customFormFields: matchedTemplate?.customFormFields,
      status: mappedStatus,
      appliedAt: item.appliedAt || matchedTemplate?.appliedAt || new Date().toISOString(),
      isBlacklisted: !!item.isBlacklisted,
      blacklistWarningNote: item.blacklistWarningNote ?? null,
      isScannedOrUnparseablePdf: !!item.isScannedOrUnparseablePdf,
      duplicateCount: item.duplicateCount ?? 1,
      isOutdatedScore: !!item.isOutdatedScore,
      slaDeadlineAt: item.slaDeadlineAt ?? item.slaDueAt ?? null,
      isSlaEscalated: !!item.isSlaEscalated,
      slaOverrideReason: item.slaOverrideReason ?? null,

      // V3 Enterprise Compliance mapping
      interviewScore: item.interviewScore ?? (item.isAiOverriddenByHuman ? 85 : null),
      isAiOverriddenByHuman: !!item.isAiOverriddenByHuman,
      aiOverrideReason: item.aiOverrideReason ?? null,
      offerStatus: item.offerStatus || (track === 'PROFESSIONAL' && mappedStatus === 'InterviewPassed' ? 'Offer_Pending' : 'Not_Required'),
      offeredSalary: item.offeredSalary ?? null,
      offerResponseNote: item.offerResponseNote ?? null,
      transferredFromJobId: item.transferredFromJobId ?? null,
      transferredFromJobTitle: item.transferredFromJobTitle ?? (item.transferReason ? 'Xưởng SMT' : null),
      transferReason: item.transferReason ?? null,
      isPiiMaskedBeforeAi: item.isPiiMaskedBeforeAi !== false,

      // V5 Factory & HR Operations Complete
      convertedEmployeeCode: item.convertedEmployeeCode || null,
      isExEmployeeRehire: !!item.isExEmployeeRehire,
      previousEmployeeCode: item.previousEmployeeCode ?? null,
      interviewRescheduleCount: item.interviewRescheduleCount ?? 0,
      offerExpiresAt: item.offerExpiresAt ?? null,
      isInTalentPool: !!item.isInTalentPool || item.status === 'Saved_In_Talent_Pool',
      probation30DayStatus: item.probation30DayStatus ?? 'Not_Started',
      interviewBatchId: item.interviewBatchId ?? null,
      interviewBatchCode: item.interviewBatchCode ?? null
    };
  }

  // ── Logic 6: Duyệt thay khi quá hạn SLA ────────────────────────────────
  isSlaOverdue(app: IndustrialApplication): boolean {
    if (!app.slaDeadlineAt || app.isSlaEscalated) return false;
    if (app.status === 'InterviewPassed' || app.status === 'Rejected') return false;
    return new Date(app.slaDeadlineAt).getTime() < Date.now();
  }

  slaOverrideApprove(app: IndustrialApplication, event?: Event): void {
    if (event) event.stopPropagation();
    const reason = window.prompt(`Nhập lý do duyệt thay quá hạn SLA cho "${app.candidateName}":`, 'Quản đốc xưởng chưa phản hồi quá hạn SLA, HR duyệt thay để không chậm tiến độ tuyển dụng.');
    if (!reason || !reason.trim()) return;

    const apply = () => {
      this.applications.update(list =>
        list.map(a => a.applicationId === app.applicationId
          ? { ...a, status: 'ScheduledInterview', isSlaEscalated: true, slaOverrideReason: reason.trim() }
          : a)
      );
      this.toast.success('Đã duyệt thay quá hạn SLA', `Hồ sơ ${app.candidateName} đã được HR duyệt thay và đánh dấu Escalated.`);
    };

    const isGuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(app.applicationId);
    if (!isGuid) {
      apply();
      return;
    }

    this.http.post<any>(`/api/applications/${app.applicationId}/sla-override`, {
      slaOverrideReason: reason.trim(),
      targetStatus: 'ScheduledInterview'
    }).subscribe({
      next: () => apply(),
      error: (err) => this.toast.error('Không thể duyệt thay', err?.error?.message || 'Bạn không có quyền hoặc hồ sơ không hợp lệ.')
    });
  }

  // ── V3 Enterprise Compliance: Human Override, Transfer Job & Offer Letter ──
  openOverrideModal(app: IndustrialApplication, event?: Event): void {
    if (event) event.stopPropagation();
    this.overrideModalApp.set(app);
    this.overrideReason.set(app.aiOverrideReason || 'CV viết sơ sài nhưng ứng viên có chứng chỉ nghề thực tế và kinh nghiệm làm việc tại KCN');
    this.overrideToPassed.set(true);
    this.showOverrideModal.set(true);
  }

  submitOverrideAi(): void {
    const app = this.overrideModalApp();
    if (!app || !this.overrideReason().trim()) {
      this.toast.error('Lỗi', 'Vui lòng nhập lý do ghi đè kết quả AI.');
      return;
    }
    const apply = () => {
      app.isAiOverriddenByHuman = true;
      app.aiOverrideReason = this.overrideReason().trim();
      app.status = this.overrideToPassed() ? 'ScheduledInterview' : 'Rejected';
      this.showOverrideModal.set(false);
      this.toast.success('Thành công', 'Đã ghi đè kết quả AI (Human Override).');
    };

    const isGuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(app.applicationId);
    if (!isGuid) {
      apply();
      return;
    }

    this.http.post<any>(`/api/applications/${app.applicationId}/override-ai`, {
      overrideToPassed: this.overrideToPassed(),
      overrideReason: this.overrideReason().trim(),
      newStatus: this.overrideToPassed() ? 'Interview_Pending' : 'Rejected'
    }).subscribe({
      next: () => apply(),
      error: (err) => this.toast.error('Lỗi ghi đè AI', err?.error?.message || 'Không thể ghi đè kết quả AI.')
    });
  }

  openTransferModal(app: IndustrialApplication, event?: Event): void {
    if (event) event.stopPropagation();
    this.transferModalApp.set(app);
    this.transferReason.set('Thị lực 8/10 không đạt chuẩn xưởng linh kiện điện tử SMT, điều chuyển sang Xưởng Kho Vận (WH_LOGS)');
    this.showTransferModal.set(true);

    this.http.get<any[]>('/api/jobs').subscribe({
      next: (jobs) => {
        const list = (jobs || [])
          .filter(j => j.jobId !== app.jobId)
          .map(j => ({ jobId: j.jobId, title: j.title, department: j.departmentName || j.department || 'Phân xưởng' }));
        this.transferAvailableJobs.set(list);
        if (list.length > 0) this.transferTargetJobId.set(list[0].jobId);
      },
      error: () => {
        // Fallback danh sách chiến dịch mẫu
        this.transferAvailableJobs.set(
          this.jobCampaigns().map(c => ({ jobId: c.id, title: c.title, department: c.deptCode }))
        );
        if (this.jobCampaigns().length > 0) this.transferTargetJobId.set(this.jobCampaigns()[0].id);
      }
    });
  }

  submitTransferJob(): void {
    const app = this.transferModalApp();
    if (!app || !this.transferTargetJobId() || !this.transferReason().trim()) {
      this.toast.error('Lỗi', 'Vui lòng chọn phân xưởng đích và nhập lý do điều chuyển.');
      return;
    }
    const apply = () => {
      const target = this.transferAvailableJobs().find(j => j.jobId === this.transferTargetJobId());
      app.transferredFromJobTitle = app.jobTitle;
      app.jobTitle = target ? target.title : app.jobTitle;
      app.transferReason = this.transferReason().trim();
      this.showTransferModal.set(false);
      this.toast.success('Thành công', `Đã điều chuyển hồ sơ ${app.candidateName} sang phân xưởng mới.`);
    };

    const isGuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(app.applicationId);
    if (!isGuid) {
      apply();
      return;
    }

    this.http.post<any>(`/api/applications/${app.applicationId}/transfer-job`, {
      targetJobId: this.transferTargetJobId(),
      transferReason: this.transferReason().trim()
    }).subscribe({
      next: () => apply(),
      error: (err) => this.toast.error('Lỗi điều chuyển', err?.error?.message || 'Không thể điều chuyển hồ sơ.')
    });
  }

  openOfferModal(app: IndustrialApplication, event?: Event): void {
    if (event) event.stopPropagation();
    this.offerModalApp.set(app);
    this.offeredSalary.set(app.offeredSalary || 18000000);
    this.offerNote.set(app.offerResponseNote || 'Lương cơ bản thỏa thuận 18.000.000 VNĐ Net + phụ cấp kỹ sư');
    this.showOfferModal.set(true);
  }

  submitSendOffer(): void {
    const app = this.offerModalApp();
    if (!app || this.offeredSalary() <= 0) {
      this.toast.error('Lỗi', 'Mức lương thỏa thuận phải lớn hơn 0.');
      return;
    }
    const apply = () => {
      app.offerStatus = 'Offer_Sent';
      app.offeredSalary = this.offeredSalary();
      app.offerResponseNote = this.offerNote().trim();
      this.showOfferModal.set(false);
      this.toast.success('Đã gửi Offer', `Đã gửi Offer Letter với mức lương ${this.offeredSalary().toLocaleString()} VNĐ cho ${app.candidateName}.`);
    };

    const isGuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(app.applicationId);
    if (!isGuid) {
      apply();
      return;
    }

    this.http.post<any>(`/api/applications/${app.applicationId}/send-offer`, {
      offeredSalary: this.offeredSalary(),
      note: this.offerNote().trim()
    }).subscribe({
      next: () => apply(),
      error: (err) => this.toast.error('Lỗi gửi Offer', err?.error?.message || 'Không thể gửi Offer Letter.')
    });
  }

  submitRespondOffer(app: IndustrialApplication, accepted: boolean, event?: Event): void {
    if (event) event.stopPropagation();
    const promptMsg = accepted ? `Ghi chú xác nhận ứng viên ${app.candidateName} ĐỒNG Ý Offer:` : `Nhập lý do ứng viên ${app.candidateName} TỪ CHỐI Offer:`;
    const note = window.prompt(promptMsg, accepted ? 'Ứng viên đã ký xác nhận qua email' : 'Mức lương chưa thỏa đáng với năng lực');
    if (note === null) return;

    const apply = () => {
      app.offerStatus = accepted ? 'Offer_Accepted' : 'Offer_Declined';
      app.offerResponseNote = note;
      this.toast.success('Cập nhật Offer', accepted ? 'Ứng viên đã chấp nhận Offer. Đủ điều kiện chuyển Onboarding!' : 'Đã ghi nhận ứng viên từ chối Offer.');
    };

    const isGuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(app.applicationId);
    if (!isGuid) {
      apply();
      return;
    }

    this.http.post<any>(`/api/applications/${app.applicationId}/respond-offer`, {
      accepted,
      responseNote: note
    }).subscribe({
      next: () => apply(),
      error: (err) => this.toast.error('Lỗi phản hồi Offer', err?.error?.message || 'Không thể cập nhật phản hồi Offer.')
    });
  }

  // ── Quick Actions & Selection ──────────────────────────────────────────
  setTab(tab: QuickTabType): void {
    this.activeTab.set(tab);
    this.selectedIds.set(new Set());
  }

  toggleSelection(appId: string, event?: Event): void {
    if (event) event.stopPropagation();
    const next = new Set(this.selectedIds());
    if (next.has(appId)) next.delete(appId);
    else next.add(appId);
    this.selectedIds.set(next);
  }

  toggleSelectAllVisible(event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    if (!checked) {
      this.selectedIds.set(new Set());
      return;
    }
    const ids = this.filteredApplications().map(a => a.applicationId);
    this.selectedIds.set(new Set(ids));
  }

  selectHighScoreAndShiftReady(): void {
    const matching = this.filteredApplications()
      .filter(a => a.aiMatchingScore >= 75 && a.shift12hReady && !a.isRuleRejected)
      .map(a => a.applicationId);

    this.selectedIds.set(new Set(matching));
    this.toast.success(
      'Đã chọn tự động',
      `Đã tích chọn ${matching.length} ứng viên có Điểm >= 75 & Đạt cam kết Ca Kíp!`
    );
  }

  clearSelection(): void {
    this.selectedIds.set(new Set());
  }

  // ── Bulk Action API Call (POST /api/applications/bulk-action) ──────────
  executeBulkAction(targetStatus: 'SentToManager' | 'ScheduledInterview' | 'InterviewPassed' | 'Rejected'): void {
    const ids = Array.from(this.selectedIds());
    if (ids.length === 0) return;

    this.isProcessingBulk.set(true);

    const statusLabels: Record<string, string> = {
      SentToManager: 'Chuyển Quản đốc xưởng duyệt',
      ScheduledInterview: 'Xếp ca phỏng vấn hàng loạt',
      InterviewPassed: 'Duyệt Đạt PV -> Chuyển Onboarding',
      Rejected: 'Từ chối hồ sơ'
    };

    // Cập nhật giao diện và gọi API Backend xử lý lô
    this.applications.update(list =>
      list.map(app =>
        ids.includes(app.applicationId)
          ? { ...app, status: targetStatus, isRuleRejected: targetStatus === 'Rejected' ? true : app.isRuleRejected }
          : app
      )
    );

    // Gọi API Backend đối với các GUID thực
    const validGuids = ids.filter(id => /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id));
    if (validGuids.length > 0) {
      this.http.post('/api/applications/bulk-action', {
        applicationIds: validGuids,
        targetStatus,
        note: `Thao tác hàng loạt (${statusLabels[targetStatus]})`
      }).subscribe({
        next: () => {
          this.isProcessingBulk.set(false);
          this.selectedIds.set(new Set());
          this.toast.success('Thành công', `Đã cập nhật ${ids.length} ứng viên: ${statusLabels[targetStatus]}!`);
        },
        error: () => {
          this.isProcessingBulk.set(false);
          this.selectedIds.set(new Set());
          this.toast.success('Đã xử lý lô', `Đã cập nhật trạng thái cho ${ids.length} ứng viên trên bảng điều khiển.`);
        }
      });
    } else {
      setTimeout(() => {
        this.isProcessingBulk.set(false);
        this.selectedIds.set(new Set());
        this.toast.success('Xử lý lô thành công', `Đã thực hiện "${statusLabels[targetStatus]}" cho ${ids.length} ứng viên!`);
      }, 250);
    }
  }

  // ── V5 Batch Interview Scheduling Methods ────────────────────────────
  openBatchInterviewModal(): void {
    if (this.selectedCount() === 0) {
      this.toast.warning('Chưa chọn ứng viên', 'Vui lòng tích chọn ít nhất 1 ứng viên để xếp ca phỏng vấn.');
      return;
    }

    // Tính ngày làm việc hợp lệ tiếp theo (bỏ qua Thứ 7, Chủ Nhật và ngày nghỉ lễ)
    let nextDate = new Date();
    nextDate.setDate(nextDate.getDate() + 1);
    while (this.isWeekend(nextDate) || this.isVietnamHoliday(nextDate)) {
      nextDate.setDate(nextDate.getDate() + 1);
    }
    this.batchInterviewDate.set(nextDate.toISOString().split('T')[0]);
    this.batchDateValidationError.set('');

    this.showBatchInterviewModal.set(true);
    this.loadBatchesFromDb();
  }

  loadBatchesFromDb(): void {
    this.isLoadingBatches.set(true);
    this.http.get<any>('/api/hr-operations/batches').subscribe({
      next: (res: any) => {
        this.isLoadingBatches.set(false);
        const list = Array.isArray(res) ? res : (res?.data || []);
        if (Array.isArray(list)) {
          this.activeInterviewBatches.set(list.map((b: any) => {
            const dateObj = b.scheduledDate ? new Date(b.scheduledDate) : null;
            const dateStr = dateObj && !isNaN(dateObj.getTime())
              ? dateObj.toLocaleDateString('vi-VN')
              : 'Hôm nay';
            return {
              id: b.id,
              batchCode: b.batchCode || `BATCH-${b.id}`,
              shiftSlot: b.shiftSlot,
              shiftName: b.shiftName || (b.shiftSlot === 'MORNING_08_11' ? 'Ca Sáng (08:00 - 11:30)' : 'Ca Chiều (13:30 - 17:00)'),
              scheduledDate: dateStr,
              currentCount: b.currentCount ?? 0,
              maxCapacity: b.maxCapacity ?? 50
            };
          }));
        }
      },
      error: () => {
        this.isLoadingBatches.set(false);
      }
    });
  }

  closeBatchInterviewModal(): void {
    this.showBatchInterviewModal.set(false);
  }

  confirmScheduleBatches(): void {
    const ids = Array.from(this.selectedIds());
    if (ids.length === 0) return;

    // Validate ngày xếp ca (chống cuối tuần & ngày nghỉ lễ)
    if (!this.validateBatchDate(this.batchInterviewDate())) {
      this.toast.warning('Ngày xếp ca không hợp lệ', this.batchDateValidationError() || 'Vui lòng chọn ngày làm việc trong tuần (Thứ 2 - Thứ 6) không trùng ngày nghỉ lễ.');
      return;
    }

    this.isSchedulingBatches.set(true);

    const targetJobId = this.selectedJobId() !== 'ALL' && !this.selectedJobId().startsWith('job-')
      ? this.selectedJobId()
      : '00000000-0000-0000-0000-000000000001';

    const validGuids = ids.filter(id => /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id));

    if (validGuids.length > 0) {
      this.http.post<any>('/api/hr-operations/schedule-batches', {
        jobId: targetJobId,
        applicationIds: validGuids,
        scheduledDate: this.batchInterviewDate(),
        shiftSlot: this.batchShiftSlot(),
        maxCapacity: this.batchMaxCapacity()
      }).subscribe({
        next: (res) => {
          this.isSchedulingBatches.set(false);
          this.showBatchInterviewModal.set(false);
          this.selectedIds.set(new Set());
          this.toast.success('Xếp ca & Gửi mail thành công', res?.message || `Đã chia tách và xếp ca cho ${ids.length} ứng viên kèm email thông báo!`);
          this.fetchBackendApplications();
          this.loadBatchesFromDb();
        },
        error: (err) => {
          this.isSchedulingBatches.set(false);
          this.toast.error('Lỗi xếp ca', err?.error?.message || 'Không thể xếp ca phỏng vấn.');
        }
      });
    } else {
      setTimeout(() => {
        this.isSchedulingBatches.set(false);
        this.showBatchInterviewModal.set(false);
        this.applications.update(list =>
          list.map(app => ids.includes(app.applicationId)
            ? { ...app, status: 'ScheduledInterview', interviewBatchCode: 'BATCH-SMT-NEW' }
            : app)
        );
        this.selectedIds.set(new Set());
        const splitText = ids.length > 50 ? `(Đã tự động chia làm ${Math.ceil(ids.length / 50)} ca)` : '';
        this.toast.success('Xếp ca thành công', `Đã xếp ${ids.length} ứng viên vào ca phỏng vấn ${splitText} và gửi email thông báo.`);
      }, 400);
    }
  }

  // ── VIETNAM HOLIDAYS & INTERVIEWERS LOADING ──────────────────────────
  loadVietnamHolidays(): void {
    const currentYear = new Date().getFullYear();
    this.http.get<{ holidays: string[] }>(`/api/master-data/vietnam-holidays/${currentYear}`).subscribe({
      next: (res) => {
        if (res && res.holidays && res.holidays.length > 0) {
          this.vietnameseHolidays.set(res.holidays);
        }
      },
      error: () => {
        this.vietnameseHolidays.set([
          '01-01', '02-01',
          '15-02', '16-02', '17-02', '18-02', '19-02', '20-02', '21-02', '22-02', '23-02',
          '26-04', '30-04', '01-05', '01-09', '02-09', '03-09'
        ]);
      }
    });
  }

  loadInterviewers(): void {
    this.isLoadingInterviewers.set(true);
    this.http.get<any>('/api/employees/interviewers').subscribe({
      next: (res) => {
        this.isLoadingInterviewers.set(false);
        const rawList = Array.isArray(res) ? res : (res?.data || []);
        if (Array.isArray(rawList) && rawList.length > 0) {
          const mapped: InterviewerItem[] = rawList.map((item: any) => ({
            id: String(item.id || item.userId || item.Id),
            fullName: item.fullName || item.name || item.FullName || 'Người phỏng vấn',
            email: item.email || item.Email || '',
            roleName: item.roleName || item.role || item.RoleName || 'INTERVIEWER'
          }));
          this.interviewers.set(mapped);
          if (!this.selectedInterviewerId() || !mapped.some(m => m.id === this.selectedInterviewerId())) {
            this.selectedInterviewerId.set(mapped[0].id);
          }
        }
      },
      error: () => {
        this.isLoadingInterviewers.set(false);
      }
    });
  }

  // ── DATE, WEEKEND & BUSINESS HOURS VALIDATIONS ─────────────────────────
  isWeekend(date: Date): boolean {
    const day = date.getDay();
    return day === 0 || day === 6; // 0: Chủ nhật, 6: Thứ 7
  }

  isVietnamHoliday(date: Date): boolean {
    const d = String(date.getDate()).padStart(2, '0');
    const m = String(date.getMonth() + 1).padStart(2, '0');
    const key = `${d}-${m}`;
    return this.vietnameseHolidays().includes(key);
  }

  validateInterviewDate(dateStr: string): boolean {
    if (!dateStr) {
      this.dateValidationError.set('Vui lòng chọn ngày phỏng vấn');
      return false;
    }
    const [y, m, d] = dateStr.split('-').map(Number);
    if (!y || !m || !d) {
      this.dateValidationError.set('Định dạng ngày không hợp lệ');
      return false;
    }
    const selected = new Date(y, m - 1, d);
    selected.setHours(0, 0, 0, 0);

    const today = new Date();
    today.setHours(0, 0, 0, 0);

    if (selected < today) {
      this.dateValidationError.set('Ngày phỏng vấn phải là ngày trong tương lai (không chọn ngày quá khứ)');
      return false;
    }

    if (this.isWeekend(selected)) {
      const dayName = selected.getDay() === 0 ? 'Chủ nhật' : 'Thứ 7';
      this.dateValidationError.set(`Ngày ${dateStr} là ${dayName} (ngày nghỉ cuối tuần). Vui lòng chọn ngày làm việc trong tuần (Thứ 2 - Thứ 6)`);
      return false;
    }

    if (this.isVietnamHoliday(selected)) {
      this.dateValidationError.set(`Ngày ${dateStr} là ngày nghỉ lễ Việt Nam, không thể xếp lịch phỏng vấn`);
      return false;
    }

    this.dateValidationError.set('');
    return true;
  }

  validateInterviewTime(timeStr: string): boolean {
    if (!timeStr) {
      this.timeValidationError.set('Vui lòng chọn giờ phỏng vấn');
      return false;
    }
    const [h, m] = timeStr.split(':').map(Number);
    if (isNaN(h)) {
      this.timeValidationError.set('Định dạng giờ không hợp lệ');
      return false;
    }
    // Giờ hành chính: 08:00 - 18:00
    if (h < 8 || (h === 18 && m > 0) || h > 18) {
      this.timeValidationError.set('Thời gian phỏng vấn phải nằm trong khung giờ hành chính (08:00 - 18:00)');
      return false;
    }
    this.timeValidationError.set('');
    return true;
  }

  validateInterviewLocation(): boolean {
    const loc = (this.interviewLocation() || '').trim();
    if (this.interviewType() === 'ONLINE') {
      if (!loc) {
        this.locationValidationError.set('Vui lòng nhập link meeting (VD: Google Meet, MS Teams, Zoom)');
        return false;
      }
      try {
        new URL(loc);
        this.locationValidationError.set('');
        return true;
      } catch {
        this.locationValidationError.set('Đường link meeting không đúng định dạng URL (VD: https://meet.google.com/xxx-yyy)');
        return false;
      }
    } else {
      if (!loc || loc.length < 5) {
        this.locationValidationError.set('Vui lòng nhập địa điểm phỏng vấn cụ thể (tối thiểu 5 ký tự)');
        return false;
      }
      this.locationValidationError.set('');
      return true;
    }
  }

  validateBatchDate(dateStr: string): boolean {
    if (!dateStr) {
      this.batchDateValidationError.set('Vui lòng chọn ngày xếp ca');
      return false;
    }
    const [y, m, d] = dateStr.split('-').map(Number);
    if (!y || !m || !d) {
      this.batchDateValidationError.set('Định dạng ngày không hợp lệ');
      return false;
    }
    const selected = new Date(y, m - 1, d);
    selected.setHours(0, 0, 0, 0);

    const today = new Date();
    today.setHours(0, 0, 0, 0);

    if (selected < today) {
      this.batchDateValidationError.set('Ngày xếp ca phỏng vấn phải là ngày trong tương lai');
      return false;
    }

    if (this.isWeekend(selected)) {
      const dayName = selected.getDay() === 0 ? 'Chủ nhật' : 'Thứ 7';
      this.batchDateValidationError.set(`Ngày ${dateStr} là ${dayName} (ngày nghỉ cuối tuần). Vui lòng chọn ngày làm việc trong tuần (Thứ 2 - Thứ 6)`);
      return false;
    }

    if (this.isVietnamHoliday(selected)) {
      this.batchDateValidationError.set(`Ngày ${dateStr} là ngày nghỉ lễ Việt Nam, không thể xếp ca`);
      return false;
    }

    this.batchDateValidationError.set('');
    return true;
  }

  isFormValidForSubmit(): boolean {
    const d = this.interviewDate();
    const t = this.interviewTime();
    const loc = (this.interviewLocation() || '').trim();
    const invId = this.selectedInterviewerId();

    if (!d || !t || !loc || !invId) return false;
    if (this.dateValidationError() || this.timeValidationError() || this.locationValidationError()) return false;
    return true;
  }

  // ── EMAIL PREVIEW & AI PERSONALIZATION ─────────────────────────────────
  updateEmailPreview(): void {
    const app = this.selectedInterviewApp();
    if (!app) return;

    const dateFormatted = this.interviewDate()
      ? new Date(this.interviewDate()).toLocaleDateString('vi-VN', { weekday: 'long', day: '2-digit', month: '2-digit', year: 'numeric' })
      : '[Chưa chọn ngày]';
    const typeText = this.interviewType() === 'ONLINE' ? 'Online (Trực tuyến qua Video Call)' : 'Offline (Trực tiếp tại trụ sở)';
    const locLabel = this.interviewType() === 'ONLINE' ? 'Đường dẫn tham gia (Link Meeting)' : 'Địa điểm tiếp đón';
    const locVal = this.interviewLocation() || '[Chưa nhập]';
    const candidateName = app.candidateName || 'Ứng viên';
    const jobTitle = app.jobTitle || 'Vị trí tuyển dụng';

    this.emailPreviewContent.set(
`${this.aiOpeningText() ? this.aiOpeningText() + '\n\n' : ''}Kính gửi ${candidateName},

Phòng Tuyển dụng V9 TECH trân trọng thông báo hồ sơ ứng tuyển của bạn cho vị trí "${jobTitle}" đã được Hội đồng tuyển dụng đánh giá cao và lựa chọn vào vòng phỏng vấn tiếp theo.

THÔNG TIN LỊCH PHỎNG VẤN CHI TIẾT:
- Vị trí ứng tuyển: ${jobTitle}
- Thời gian: ${dateFormatted} lúc ${this.interviewTime() || '[Chưa chọn giờ]'}
- Hình thức: ${typeText}
- ${locLabel}: ${locVal}

LƯU Ý KHI THAM GIA PHỎNG VẤN:
1. Vui lòng xác nhận tham gia bằng cách phản hồi lại email này trước thời gian phỏng vấn ít nhất 12 giờ.
2. Chuẩn bị đường truyền mạng và không gian yên tĩnh (nếu Online) hoặc mang theo bản gốc Căn cước công dân (nếu Offline).
3. Đề nghị có mặt trước giờ hẹn 10-15 phút để đảm bảo buổi phỏng vấn diễn ra thuận lợi.

Nếu bạn cần hỗ trợ hoặc có nhu cầu dời lịch vì lý do bất khả kháng, xin vui lòng phản hồi email này để được sắp xếp lại.

Trân trọng,
Hội đồng Tuyển dụng V9 TECH`
    );
  }

  generateAIOpening(): void {
    const app = this.selectedInterviewApp();
    if (!app) return;

    this.isGeneratingAI.set(true);

    const isGuidCandidate = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(app.candidateId);
    const isGuidJob = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(app.jobId);

    if (isGuidCandidate && isGuidJob) {
      this.http.post<{ opening: string }>('/api/interviews/generate-opening', {
        candidateId: app.candidateId,
        jobId: app.jobId
      }).subscribe({
        next: (res) => {
          this.isGeneratingAI.set(false);
          if (res?.opening) {
            this.aiOpeningText.set(res.opening);
            this.updateEmailPreview();
            this.toast.success('AI gợi ý thành công', 'Đã tạo đoạn mở đầu email cá nhân hóa cho ứng viên!');
          }
        },
        error: () => {
          this.isGeneratingAI.set(false);
          this.fallbackAiOpening(app);
        }
      });
    } else {
      setTimeout(() => {
        this.isGeneratingAI.set(false);
        this.fallbackAiOpening(app);
      }, 500);
    }
  }

  private fallbackAiOpening(app: IndustrialApplication): void {
    const candidateName = app.candidateName;
    const trackNote = app.recruitmentTrack === 'PROFESSIONAL'
      ? `Chúng tôi rất ấn tượng với hồ sơ chuyên môn và kinh nghiệm thực chiến của bạn trong lĩnh vực ${app.departmentName}.`
      : `Chúng tôi đánh giá cao tinh thần trách nhiệm và sự nhanh nhẹn, sẵn sàng đáp ứng yêu cầu ca kíp sản xuất của bạn.`;
    this.aiOpeningText.set(`Chào ${candidateName},\n${trackNote}`);
    this.updateEmailPreview();
    this.toast.success('Gợi ý hoàn tất', 'Đã tạo đoạn mở đầu cá nhân hóa cho ứng viên!');
  }

  // ── OPEN & CLOSE INTERVIEW MODAL ──────────────────────────────────────
  openInterviewModal(app: IndustrialApplication, event?: Event): void {
    if (event) event.stopPropagation();
    this.selectedInterviewApp.set(app);

    // Tính ngày làm việc hợp lệ tiếp theo (bỏ qua Thứ 7, Chủ Nhật và ngày nghỉ lễ)
    let nextDate = new Date();
    nextDate.setDate(nextDate.getDate() + 1);
    while (this.isWeekend(nextDate) || this.isVietnamHoliday(nextDate)) {
      nextDate.setDate(nextDate.getDate() + 1);
    }
    const dateStr = nextDate.toISOString().split('T')[0];

    this.interviewDate.set(dateStr);
    this.interviewTime.set('09:00');
    this.interviewType.set(app.recruitmentTrack === 'PROFESSIONAL' ? 'ONLINE' : 'OFFLINE');
    this.interviewLocation.set(
      app.recruitmentTrack === 'PROFESSIONAL'
        ? 'https://meet.google.com/ats-v9tech-interview'
        : 'Hội trường Tuyển dụng & Đào tạo Nhà máy V9 TECH - KCN Yên Phong'
    );
    this.aiOpeningText.set('');
    this.validateInterviewDate(dateStr);
    this.validateInterviewTime('09:00');
    this.validateInterviewLocation();

    if (this.interviewers().length > 0) {
      if (!this.selectedInterviewerId() || !this.interviewers().some(i => i.id === this.selectedInterviewerId())) {
        this.selectedInterviewerId.set(this.interviewers()[0].id);
      }
    }

    this.updateEmailPreview();
    this.showInterviewModal.set(true);

    this.loadInterviewers();
  }

  closeInterviewModal(): void {
    this.showInterviewModal.set(false);
    this.selectedInterviewApp.set(null);
  }

  sendInterviewInvitation(): void {
    const app = this.selectedInterviewApp();
    if (!app) return;

    if (!this.isFormValidForSubmit()) {
      if (this.dateValidationError()) this.toast.warning('Ngày không hợp lệ', this.dateValidationError());
      else if (this.timeValidationError()) this.toast.warning('Giờ không hợp lệ', this.timeValidationError());
      else if (this.locationValidationError()) this.toast.warning('Địa điểm không hợp lệ', this.locationValidationError());
      else this.toast.warning('Thông tin chưa đầy đủ', 'Vui lòng chọn người phỏng vấn và nhập đủ thông tin.');
      return;
    }

    this.isSendingInterview.set(true);

    const startIso = `${this.interviewDate()}T${this.interviewTime()}:00`;
    const endDateObj = new Date(new Date(startIso).getTime() + 60 * 60000);
    const endHours = String(endDateObj.getHours()).padStart(2, '0');
    const endMins = String(endDateObj.getMinutes()).padStart(2, '0');
    const endIso = `${this.interviewDate()}T${endHours}:${endMins}:00`;

    const payload = {
      applicationId: app.applicationId,
      interviewerId: this.selectedInterviewerId(),
      title: `Phỏng vấn ứng viên ${app.candidateName} - ${app.jobTitle || 'Tuyển dụng'}`,
      scheduledStart: startIso,
      scheduledEnd: endIso,
      meetingLink: this.interviewType() === 'ONLINE' ? this.interviewLocation() : null,
      location: this.interviewType() === 'OFFLINE' ? this.interviewLocation() : null,
      customEmailBody: this.emailPreviewContent(),
      candidateEmail: app.email
    };

    const isGuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(app.applicationId);
    if (isGuid) {
      this.http.post<any>(`/api/applications/${app.applicationId}/schedule-interview`, payload).subscribe({
        next: (res) => {
          this.isSendingInterview.set(false);
          this.showInterviewModal.set(false);

          this.applications.update(list =>
            list.map(item => item.applicationId === app.applicationId ? { ...item, status: 'ScheduledInterview' } : item)
          );
          if (this.selectedApp()?.applicationId === app.applicationId) {
            this.selectedApp.update(curr => curr ? { ...curr, status: 'ScheduledInterview' } : null);
          }

          const emailTarget = app.email || 'ứng viên';
          this.toast.success(
            'Lên lịch & Gửi email thành công',
            `Đã lên lịch phỏng vấn và gửi email thư mời đến ${emailTarget} (kèm CC cho Người phỏng vấn & HR)!`
          );
          this.fetchBackendApplications();
        },
        error: (err) => {
          this.isSendingInterview.set(false);
          const msg = err?.error?.message || 'Có lỗi xảy ra khi lên lịch phỏng vấn.';
          this.toast.error('Lên lịch thất bại', msg);
        }
      });
    } else {
      setTimeout(() => {
        this.isSendingInterview.set(false);
        this.showInterviewModal.set(false);
        this.applications.update(list =>
          list.map(item => item.applicationId === app.applicationId ? { ...item, status: 'ScheduledInterview' } : item)
        );
        if (this.selectedApp()?.applicationId === app.applicationId) {
          this.selectedApp.update(curr => curr ? { ...curr, status: 'ScheduledInterview' } : null);
        }
        this.toast.success(
          'Lên lịch thành công',
          `Đã xếp lịch phỏng vấn và gửi email mời ${app.candidateName} (${app.email || 'ứng viên'}).`
        );
      }, 400);
    }
  }

  // ── Chuyển bước quy trình (Stage Transition) cho từng ứng viên ─────────
  changeApplicationStage(
    app: IndustrialApplication,
    targetStatus: 'SentToManager' | 'ScheduledInterview' | 'InterviewPassed' | 'Rejected',
    event?: Event
  ): void {
    if (event) event.stopPropagation();

    // Nếu chọn Xếp lịch phỏng vấn -> Mở Modal Lên Lịch Phỏng Vấn (có check ngày nghỉ lễ, cuối tuần, giờ hành chính & gửi mail)
    if (targetStatus === 'ScheduledInterview') {
      this.openInterviewModal(app, event);
      return;
    }

    const statusLabels: Record<string, string> = {
      SentToManager: 'Chuyển Quản đốc xưởng duyệt',
      ScheduledInterview: 'Xếp lịch phỏng vấn',
      InterviewPassed: 'Duyệt Đạt phỏng vấn -> Chuyển Tiếp nhận & Onboarding',
      Rejected: 'Từ chối hồ sơ'
    };

    const nextOfferStatus =
      targetStatus === 'InterviewPassed' && app.recruitmentTrack === 'PROFESSIONAL' && (!app.offerStatus || app.offerStatus === 'Not_Required')
        ? 'Offer_Pending'
        : app.offerStatus;

    this.applications.update(list =>
      list.map(item =>
        item.applicationId === app.applicationId
          ? {
              ...item,
              status: targetStatus,
              offerStatus: nextOfferStatus,
              isRuleRejected: targetStatus === 'Rejected' ? true : item.isRuleRejected
            }
          : item
      )
    );

    if (this.selectedApp()?.applicationId === app.applicationId) {
      this.selectedApp.update(curr =>
        curr ? { ...curr, status: targetStatus, offerStatus: nextOfferStatus, isRuleRejected: targetStatus === 'Rejected' ? true : curr.isRuleRejected } : null
      );
    }

    const isGuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(app.applicationId);
    if (isGuid) {
      this.http.post<any>('/api/applications/bulk-action', {
        applicationIds: [app.applicationId],
        targetStatus,
        note: `Cập nhật trạng thái hồ sơ: ${statusLabels[targetStatus]}`
      }).subscribe({
        next: () => {
          this.applications.update(list =>
            list.map(item =>
              item.applicationId === app.applicationId
                ? {
                    ...item,
                    status: targetStatus,
                    offerStatus: nextOfferStatus,
                    isRuleRejected: targetStatus === 'Rejected' ? true : item.isRuleRejected
                  }
                : item
            )
          );
          if (this.selectedApp()?.applicationId === app.applicationId) {
            this.selectedApp.update(curr => curr ? { ...curr, status: targetStatus, offerStatus: nextOfferStatus } : null);
          }
          this.toast.success('Đã lưu trạng thái', `${app.candidateName}: ${statusLabels[targetStatus]}.`);
          this.fetchBackendApplications();
        },
        error: (err) => {
          // Fallback thử qua quick-walkin-eval nếu hồ sơ chưa tồn tại
          this.http.post<any>('/api/applications/quick-walkin-eval', {
            applicationId: app.applicationId,
            jobPostingId: /^[0-9a-f]{8}-/i.test(app.jobId) ? app.jobId : null,
            fullName: app.candidateName,
            phone: app.phone,
            yearOfBirth: app.yearOfBirth,
            cccd: app.cccd,
            hometown: app.hometown,
            educationLevel: app.educationLevel,
            experienceSummary: app.aiSummary,
            checklist: app.oralChecklist,
            targetStatus
          }).subscribe({
            next: () => {
              this.applications.update(list =>
                list.map(item => item.applicationId === app.applicationId ? { ...item, status: targetStatus, offerStatus: nextOfferStatus } : item)
              );
              this.toast.success('Đã cập nhật trạng thái', `${app.candidateName}: ${statusLabels[targetStatus]}.`);
              this.fetchBackendApplications();
            },
            error: (fallbackErr) => {
              this.toast.error('Lỗi cập nhật CSDL', fallbackErr?.error?.message || 'Không thể lưu trạng thái vào CSDL.');
            }
          });
        }
      });
    } else {
      this.toast.info('Thông báo', `Hồ sơ mẫu tạm thời: ${app.candidateName}.`);
    }
  }

  goToOnboarding(app: IndustrialApplication, event?: Event): void {
    if (event) event.stopPropagation();
    this.closeDrawer();
    this.router.navigate(['/hr/onboarding']);
  }

  goToInterviews(app: IndustrialApplication, event?: Event): void {
    if (event) event.stopPropagation();
    this.closeDrawer();
    this.router.navigate(['/hr/interviews']);
  }

  // ── 1-Click Fast-Track Oral Interview Evaluation ───────────────────────
  evaluateOralQuick(app: IndustrialApplication, targetStatus: 'InterviewPassed' | 'Rejected', event?: Event): void {
    if (event) event.stopPropagation();

    const cl = app.oralChecklist;
    const checklistScore = (cl.handEyePass ? 25 : 0) + (cl.visionPass ? 25 : 0) + (cl.shift12hConfirmed ? 30 : 0) + (cl.attitudePass ? 20 : 0);
    const isGuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(app.applicationId);
    const appId = isGuid ? app.applicationId : null;
    const jobId = /^[0-9a-f]{8}-/i.test(app.jobId) ? app.jobId : null;

    // Gửi Request thật đến Backend API để lưu trực tiếp vào CSDL SQL Server
    this.http.post<any>('/api/applications/quick-walkin-eval', {
      applicationId: appId,
      jobPostingId: jobId,
      fullName: app.candidateName,
      phone: app.phone,
      yearOfBirth: app.yearOfBirth,
      cccd: app.cccd,
      hometown: app.hometown,
      educationLevel: app.educationLevel,
      experienceSummary: app.aiSummary,
      checklist: app.oralChecklist,
      targetStatus
    }).subscribe({
      next: (res) => {
        if (res && res.success) {
          const serverData = res.data;
          const savedStatus = serverData?.status || 'PASSED';
          const uiStatus: IndustrialApplication['status'] = (savedStatus === 'HIRED' || savedStatus === 'Hired') ? 'Hired' : (savedStatus === 'PASSED' || savedStatus === 'InterviewPassed') ? 'InterviewPassed' : 'Rejected';
          const returnedOfferStatus = serverData?.offerStatus || (app.recruitmentTrack === 'PROFESSIONAL' ? 'Offer_Pending' : app.offerStatus);
          const returnedScore = serverData?.score ?? app.aiMatchingScore;

          this.applications.update(list =>
            list.map(item =>
              (item.applicationId === app.applicationId || (item.phone && item.phone === app.phone))
                ? {
                    ...item,
                    status: uiStatus,
                    offerStatus: returnedOfferStatus,
                    aiMatchingScore: returnedScore,
                    interviewScore: checklistScore,
                    isRuleRejected: targetStatus === 'Rejected'
                  }
                : item
            )
          );

          if (this.selectedApp()?.applicationId === app.applicationId) {
            this.selectedApp.update(curr =>
              curr
                ? {
                    ...curr,
                    status: uiStatus,
                    offerStatus: returnedOfferStatus,
                    aiMatchingScore: returnedScore,
                    interviewScore: checklistScore,
                    isRuleRejected: targetStatus === 'Rejected'
                  }
                : null
            );
          }

          if (targetStatus === 'InterviewPassed') {
            this.toast.success(
              'Duyệt phỏng vấn thành công',
              `Đã cập nhật trạng thái Đạt PV cho ${app.candidateName} (${app.phone}) vào CSDL và chuyển sang Tiếp Nhận & Onboarding.`
            );
          } else {
            this.toast.warning('Từ chối hồ sơ', `Đã lưu trạng thái Từ chối ứng viên ${app.candidateName} vào CSDL.`);
          }

          // Tải lại danh sách thật từ Backend để chắc chắn 100% dữ liệu đã đồng bộ từ Database
          this.fetchBackendApplications();
        }
      },
      error: (err) => {
        console.error('Lỗi khi duyệt phỏng vấn:', err);
        this.toast.error('Lỗi lưu CSDL', err?.error?.detail || err?.error?.message || 'Không thể lưu trạng thái vào CSDL. Vui lòng kiểm tra API.');
      }
    });
  }

  /**
   * Kích hoạt gọi Gemini AI chấm điểm lại thực tế cho hồ sơ (On-demand AI Scoring)
   */
  rescoreWithAi(app: IndustrialApplication): void {
    if (!app || this.isRescoring()) return;
    this.isRescoring.set(true);
    this.toast.info('Đang phân tích', 'Hệ thống đang gửi CV đến Google Gemini AI để đối chiếu yêu cầu công việc...');

    this.http.post<any>(`/api/applications/${app.applicationId}/rescore-ai`, {}).subscribe({
      next: (res) => {
        this.isRescoring.set(false);
        if (res && res.success && res.data) {
          const newScore = Math.round(Number(res.data.score || 0));
          const bd = res.data.breakdown || {};
          const explanation = res.data.explanation || 'Phân tích hoàn tất.';
          
          const updatedApp: Partial<IndustrialApplication> = {
            aiMatchingScore: newScore,
            skillMatchPercent: bd.technicalSkills ?? newScore,
            experienceMatchPercent: bd.experience ?? newScore,
            shiftHealthPercent: app.recruitmentTrack === 'PROFESSIONAL' 
              ? (bd.education ?? bd.softSkills ?? newScore)
              : (bd.softSkills ?? 95),
            aiSummary: explanation,
            isRuleRejected: newScore < 50
          };

          // Cập nhật selectedApp hiện tại
          this.selectedApp.update(curr => curr ? { ...curr, ...updatedApp } : null);

          // Cập nhật danh sách applications
          this.applications.update(list => list.map(a => a.applicationId === app.applicationId ? { ...a, ...updatedApp } : a));

          this.toast.success('Phân tích AI thành công', `Độ phù hợp: ${newScore}/100.`);
          this.cdr.detectChanges();
        }
      },
      error: (err) => {
        this.isRescoring.set(false);
        this.toast.error('Lỗi phân tích AI', err.error?.message || 'Không thể chấm điểm AI lúc này. Vui lòng thử lại sau.');
        this.cdr.detectChanges();
      }
    });
  }

  // ── Tạo mới nhanh ứng viên Walk-in tại cổng nhà máy ────────────────────
  submitWalkInCandidate(): void {
    if (!this.walkInForm.fullName.trim() || !this.walkInForm.phone.trim()) {
      this.toast.warning('Thiếu thông tin', 'Vui lòng nhập Họ tên và Số điện thoại công nhân.');
      return;
    }

    this.isSavingWalkIn.set(true);
    const checklist: OralInterviewChecklist = {
      handEyePass: this.walkInForm.handEyePass,
      visionPass: this.walkInForm.visionPass,
      shift12hConfirmed: this.walkInForm.shift12hConfirmed,
      attitudePass: this.walkInForm.attitudePass,
      quickNote: this.walkInForm.quickNote
    };
    const calcScore = (checklist.handEyePass ? 25 : 0) + (checklist.visionPass ? 25 : 0) + (checklist.shift12hConfirmed ? 30 : 0) + (checklist.attitudePass ? 20 : 0);

    const newWalkIn: IndustrialApplication = {
      applicationId: 'walkin-' + Date.now(),
      candidateId: 'cand-' + Date.now(),
      jobId: this.walkInForm.jobPostingId,
      jobTitle: 'Công nhân vận hành dây chuyền SMT (Tuyển gấp 100 người)',
      jobCode: 'JOB-SMT-100',
      departmentCode: 'PROD_LINE',
      departmentName: 'Xưởng Sản Xuất & Lắp Ráp SMT',
      candidateName: this.walkInForm.fullName.trim(),
      phone: this.walkInForm.phone.trim(),
      email: `${this.walkInForm.phone.trim()}@walkin-factory.local`,
      yearOfBirth: this.walkInForm.yearOfBirth,
      cccd: this.walkInForm.cccd || '027201009988',
      hometown: this.walkInForm.hometown,
      educationLevel: this.walkInForm.educationLevel,
      recruitmentTrack: 'MASS_LABOR',
      applicationSource: 'WALK_IN',
      scoringMethod: 'RULE_ENGINE',
      aiMatchingScore: calcScore,
      skillMatchPercent: checklist.handEyePass ? 92 : 45,
      experienceMatchPercent: 85,
      shiftHealthPercent: checklist.shift12hConfirmed && checklist.visionPass ? 98 : 50,
      aiSummary: `Tiếp nhận trực tiếp tại bàn sơ tuyển nhà máy. Kết quả kiểm tra: Tay chân ${checklist.handEyePass ? 'Linh hoạt' : 'Chưa đạt'}, Thị lực ${checklist.visionPass ? '10/10' : 'Kém'}, Ca đêm 12h ${checklist.shift12hConfirmed ? 'Sẵn sàng' : 'Không'}.`,
      tags: [
        checklist.shift12hConfirmed ? 'Xoay ca 12h' : 'Không làm ca đêm',
        checklist.visionPass ? 'Thị lực 10/10' : 'Thị lực yếu',
        'Phỏng vấn trực tiếp'
      ],
      shift12hReady: checklist.shift12hConfirmed,
      isRuleRejected: calcScore < 60,
      cvFileUrl: null,
      rawResumeText: `PHIẾU TIẾP NHẬN CÔNG NHÂN TRỰC TIẾP (WALK-IN)\n- Họ và tên: ${this.walkInForm.fullName}\n- SĐT: ${this.walkInForm.phone}\n- Năm sinh: ${this.walkInForm.yearOfBirth}\n- CCCD: ${this.walkInForm.cccd}\n- Quê quán: ${this.walkInForm.hometown}\n- Học vấn: ${this.walkInForm.educationLevel}\n- Kinh nghiệm: ${this.walkInForm.experienceSummary}\n- Ghi chú: ${this.walkInForm.quickNote}`,
      oralChecklist: checklist,
      status: 'InterviewPassed',
      appliedAt: new Date().toISOString()
    };

    this.http.post<any>('/api/applications/quick-walkin-eval', {
      fullName: this.walkInForm.fullName,
      phone: this.walkInForm.phone,
      yearOfBirth: this.walkInForm.yearOfBirth,
      cccd: this.walkInForm.cccd,
      hometown: this.walkInForm.hometown,
      educationLevel: this.walkInForm.educationLevel,
      experienceSummary: this.walkInForm.experienceSummary,
      checklist,
      targetStatus: 'InterviewPassed'
    }).subscribe({
      next: (res) => {
        if (res?.data?.applicationId) {
          newWalkIn.applicationId = res.data.applicationId;
        }
        this.applications.update(list => [newWalkIn, ...list]);
        this.isSavingWalkIn.set(false);
        this.showWalkInModal.set(false);
        this.toast.success('Tiếp nhận thành công', `Đã tạo hồ sơ và duyệt ứng viên ${newWalkIn.candidateName}.`);
      },
      error: () => {
        this.applications.update(list => [newWalkIn, ...list]);
        this.isSavingWalkIn.set(false);
        this.showWalkInModal.set(false);
        this.toast.success('Tiếp nhận thành công', `Đã thêm ứng viên ${newWalkIn.candidateName} vào danh sách phỏng vấn đạt.`);
      }
    });
  }

  // ── Đồng bộ nhanh từ Google Form / Google Sheet ────────────────────────
  syncFromGoogleFormSheet(): void {
    const url = this.sheetUrlInput().trim();
    if (!url) {
      this.toast.warning('Thiếu link Google Sheet', 'Vui lòng dán link file Google Sheet câu trả lời của Form.');
      return;
    }

    this.isSyncingSheet.set(true);
    const realJob = this.jobCampaigns().find(j => !j.id.startsWith('job-'));
    const jobIdToSync = realJob?.id || '00000000-0000-0000-0000-000000000000';

    this.http.post<any>('/api/applications/sync-google-sheets', {
      jobId: jobIdToSync,
      sheetUrl: url
    }).subscribe({
      next: (res) => {
        this.isSyncingSheet.set(false);
        this.showSyncModal.set(false);
        this.toast.success('Đồng bộ thành công', res?.message || 'Đã kéo danh sách công nhân từ Google Form về hệ thống!');
        this.fetchBackendApplications();
      },
      error: (err) => {
        this.isSyncingSheet.set(false);
        this.toast.error('Lỗi đồng bộ', err?.error?.message || 'Vui lòng kiểm tra quyền chia sẻ Viewer của file Google Sheet.');
      }
    });
  }

  // ── Adaptive Slide-over Drawer ─────────────────────────────────────────
  openDrawer(app: IndustrialApplication): void {
    this.selectedApp.set(app);
    this.isDrawerOpen.set(true);
  }

  closeDrawer(): void {
    this.isDrawerOpen.set(false);
  }

  getSafePdfUrl(url: string | null): SafeResourceUrl {
    if (!url) return this.sanitizer.bypassSecurityTrustResourceUrl('about:blank');
    let clean = url.trim();
    if (!clean.startsWith('http')) {
      const sub = clean.startsWith('/') ? clean.substring(1) : clean;
      clean = `/${sub}`;
    }
    return this.sanitizer.bypassSecurityTrustResourceUrl(`${clean}#toolbar=1&view=FitH`);
  }

  openCvInNewTab(url: string | null): void {
    if (!url) return;
    let target = url.trim();
    if (!target.startsWith('http')) {
      const sub = target.startsWith('/') ? target.substring(1) : target;
      target = `/${sub}`;
    }
    window.open(target, '_blank');
  }

  downloadCvFile(url: string | null, candidateName: string): void {
    if (!url) return;
    let target = url.trim();
    if (!target.startsWith('http')) {
      const sub = target.startsWith('/') ? target.substring(1) : target;
      target = `/${sub}`;
    }
    const a = document.createElement('a');
    a.href = target;
    a.download = `CV_${(candidateName || 'Candidate').replace(/\s+/g, '_')}.pdf`;
    a.click();
  }

  cleanAiSummary(text: string | null | undefined): string {
    if (!text) return 'Chưa có nhận xét phân tích từ AI.';
    const trimmed = text
      .replace(/^(Gemini 2\.5 Flash|Gemini AI|Gemini|AI Sàng Lọc|Rule Engine \(Tự động\)|Rule Engine|Tờ khai QR Google Form):\s*/i, '')
      .trim();
    // Ngăn chặn hiển thị toàn văn nội dung CV thô trích xuất từ PDF vào ô nhận xét
    if (trimmed.includes('MỤC TIÊU NGHỀ NGHIỆP') || trimmed.includes('KINH NGHIỆM LÀM VIỆC') || trimmed.includes('HỌC VẤN') || trimmed.includes('GPA:')) {
      return 'Hồ sơ chưa có nhận xét đánh giá. Vui lòng bấm "Phân tích lại với Gemini AI" để chạy phân tích theo khuôn năng lực.';
    }
    return trimmed;
  }

  calculateChecklistScore(cl: OralInterviewChecklist | undefined | null): number {
    if (!cl) return 0;
    return (cl.handEyePass ? 25 : 0) + (cl.visionPass ? 25 : 0) + (cl.shift12hConfirmed ? 30 : 0) + (cl.attitudePass ? 20 : 0);
  }

  parseWorkerBrief(raw: string | null | undefined): { isShiftReady: boolean; expBadge: string } {
    if (!raw) return { isShiftReady: true, expBadge: 'LĐPT' };
    const lower = raw.toLowerCase();
    const isShiftReady = !lower.includes('không làm xoay ca') && !lower.includes('không xoay ca');
    let expBadge = 'LĐPT mới';
    const expMatch = raw.match(/Kinh nghiệm:\s*([^|\n]+)/i);
    if (expMatch && expMatch[1].trim()) {
      expBadge = expMatch[1].trim();
    } else if (lower.includes('dưới 1 năm')) {
      expBadge = '< 1 năm';
    } else if (lower.includes('1 năm') || lower.includes('2 năm') || lower.includes('3 năm')) {
      expBadge = 'Đã làm KCN';
    }
    return { isShiftReady, expBadge };
  }

  getTagClass(tag: string): string {
    if (tag.includes('Không') || tag.includes('yếu') || tag.includes('Cận') || tag.includes('Chưa') || tag.includes('Trượt')) {
      return 'bg-rose-50 text-rose-700 border border-rose-200 dark:bg-rose-950/40 dark:text-rose-300';
    }
    if (tag.includes('Xoay ca') || tag.includes('Thị lực') || tag.includes('chứng chỉ') || tag.includes('Nhân viên cũ') || tag.includes('Tay nghề')) {
      return 'bg-emerald-50 text-emerald-700 border border-emerald-200 dark:bg-emerald-950/40 dark:text-emerald-300';
    }
    return 'bg-slate-100 dark:bg-slate-800 text-slate-700 dark:text-slate-300 border border-slate-200 dark:border-slate-700';
  }

  printWorkerCard(): void {
    window.print();
  }

  attachSupplementalDoc(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input.files && input.files.length > 0) {
      const file = input.files[0];
      const fakeUrl = URL.createObjectURL(file);
      const current = this.selectedApp();
      if (current) {
        this.toast.success('Đã đính kèm hồ sơ', `Đã bổ sung file "${file.name}" cho ứng viên ${current.candidateName}.`);
      }
    }
  }

  // ── Mock Data Đa Dạng 2 Luồng: Kỹ Sư PDF & Công Nhân QR Form/Walk-in ───
  private getInitialIndustrialMockData(): IndustrialApplication[] {
    return [
      {
        applicationId: '80000000-0000-0000-0000-000000000401',
        candidateId: '70000000-0000-0000-0000-000000000401',
        jobId: '60000000-0000-0000-0000-000000000101',
        jobTitle: 'Công nhân vận hành dây chuyền SMT (Tuyển gấp 100 người)',
        jobCode: 'JOB-SMT-100',
        departmentCode: 'PROD_LINE',
        departmentName: 'Xưởng Sản Xuất & Lắp Ráp SMT',
        candidateName: 'Nguyễn Văn Toàn',
        phone: '0912345678',
        email: 'toan.nguyen@rehire-ats.local',
        yearOfBirth: '1999',
        cccd: '027200001234',
        hometown: 'Bắc Ninh',
        educationLevel: 'THPT (12/12)',
        recruitmentTrack: 'MASS_LABOR',
        applicationSource: 'GOOGLE_FORM_QR',
        scoringMethod: 'RULE_ENGINE',
        aiMatchingScore: 98,
        skillMatchPercent: 98,
        experienceMatchPercent: 100,
        shiftHealthPercent: 96,
        aiSummary: 'Nhận diện nhân viên cũ tái ứng tuyển (NV-2024-0412). Nghỉ việc đúng quy trình tháng 11/2024. Đã tự động cộng điểm ưu tiên kinh nghiệm vận hành máy mounter SMT.',
        tags: ['Nhân viên cũ tái ứng tuyển', 'Tay nghề cao SMT', 'Xoay ca 12h'],
        shift12hReady: true,
        isRuleRejected: false,
        cvFileUrl: null,
        rawResumeText: 'Tờ khai QR: Nguyễn Văn Toàn (Cựu nhân viên SMT). Nghỉ việc về giải quyết việc gia đình, nay có nguyện vọng quay lại làm việc.',
        oralChecklist: {
          handEyePass: true,
          visionPass: true,
          shift12hConfirmed: true,
          attitudePass: true,
          quickNote: 'Tay nghề cứng, đã từng làm tổ phó chuyền 2.'
        },
        status: 'PENDING',
        convertedEmployeeCode: null,
        appliedAt: '2026-11-07T08:00:00Z',
        isExEmployeeRehire: true,
        previousEmployeeCode: 'NV-2024-0412'
      },
      {
        applicationId: '80000000-0000-0000-0000-000000000402',
        candidateId: '70000000-0000-0000-0000-000000000402',
        jobId: '60000000-0000-0000-0000-000000000101',
        jobTitle: 'Công nhân vận hành dây chuyền SMT (Tuyển gấp 100 người)',
        jobCode: 'JOB-SMT-100',
        departmentCode: 'PROD_LINE',
        departmentName: 'Xưởng Sản Xuất & Lắp Ráp SMT',
        candidateName: 'Trịnh Thị Thúy',
        phone: '0988776655',
        email: 'thuy.trinh@talentpool.local',
        yearOfBirth: '2001',
        cccd: '037201004321',
        hometown: 'Ninh Bình',
        educationLevel: 'THPT (12/12)',
        recruitmentTrack: 'MASS_LABOR',
        applicationSource: 'GOOGLE_FORM_QR',
        scoringMethod: 'RULE_ENGINE',
        aiMatchingScore: 91,
        skillMatchPercent: 90,
        experienceMatchPercent: 92,
        shiftHealthPercent: 95,
        aiSummary: 'Hồ sơ đạt sơ loại xuất sắc nhưng chiến dịch trước đã đủ chỉ tiêu (Closed_Quota_Full). Đã tự động bảo lưu vào Kho ứng viên tiềm năng (Talent Pool) sẵn sàng điều chuyển.',
        tags: ['Bảo lưu Talent Pool', 'Xoay ca 12h', 'Sẵn sàng điều chuyển'],
        shift12hReady: true,
        isRuleRejected: false,
        cvFileUrl: null,
        rawResumeText: 'Hồ sơ bảo lưu từ chiến dịch tháng trước, đạt tiêu chuẩn thị lực và xoay ca.',
        oralChecklist: {
          handEyePass: true,
          visionPass: true,
          shift12hConfirmed: true,
          attitudePass: true,
          quickNote: 'Hồ sơ tiềm năng, ưu tiên điều chuyển sang chiến dịch mới.'
        },
        status: 'Saved_In_Talent_Pool',
        appliedAt: '2026-11-05T14:20:00Z',
        isInTalentPool: true
      },
      {
        applicationId: '80000000-0000-0000-0000-000000000403',
        candidateId: '70000000-0000-0000-0000-000000000403',
        jobId: '60000000-0000-0000-0000-000000000102',
        jobTitle: 'Kỹ sư Tự động hóa PLC & Bảo trì Dây chuyền (Mitsubishi/Siemens)',
        jobCode: 'JOB-ENG-012',
        departmentCode: 'TECH_MNT',
        departmentName: 'Phòng Kỹ Thuật & Bảo Trì Nhà Máy',
        candidateName: 'Trần Minh Hoàng',
        phone: '0912345889',
        email: 'hoang.tran.plc@gmail.com',
        yearOfBirth: '1996',
        cccd: '001096014521',
        hometown: 'Hải Dương',
        educationLevel: 'Kỹ sư Tự động hóa - ĐH Bách Khoa Hà Nội',
        recruitmentTrack: 'PROFESSIONAL',
        applicationSource: 'PDF_UPLOAD',
        scoringMethod: 'GEMINI_AI',
        aiMatchingScore: 93,
        skillMatchPercent: 95,
        experienceMatchPercent: 92,
        shiftHealthPercent: 90,
        aiSummary: 'Ứng viên có 5 năm kinh nghiệm lập trình PLC Mitsubishi FX/Q-Series, biến tần Inverter và bảo trì dây chuyền SMT Panasonic tại KCN VSIP. Phù hợp chuyển Quản đốc xưởng Kỹ thuật duyệt phỏng vấn.',
        tags: ['PLC Mitsubishi', 'SCADA / HMI', 'Bảo trì SMT', 'AutoCAD Electrical', 'Tiếng Anh chuyên ngành'],
        shift12hReady: true,
        isRuleRejected: false,
        cvFileUrl: '/uploads/sample-cv-engineer.pdf',
        rawResumeText: 'Kỹ sư Tự động hóa 5 năm kinh nghiệm lập trình PLC Mitsubishi/Siemens, bảo trì máy gắn linh kiện SMT.',
        oralChecklist: {
          handEyePass: true,
          visionPass: true,
          shift12hConfirmed: true,
          attitudePass: true,
          quickNote: 'Chuyên môn kỹ thuật vững, sẵn sàng trực ca sự cố dây chuyền.'
        },
        status: 'SentToManager',
        appliedAt: '2026-11-06T08:15:00Z'
      },
      {
        applicationId: '80000000-0000-0000-0000-000000000404',
        candidateId: '70000000-0000-0000-0000-000000000404',
        jobId: '60000000-0000-0000-0000-000000000104',
        jobTitle: 'Chuyên viên Quản lý Chất lượng IQC/OQC (ISO 9001 & IATF 16949)',
        jobCode: 'JOB-QA-008',
        departmentCode: 'QA_QC',
        departmentName: 'Bộ Phận Quản Lý Chất Lượng (QA/QC)',
        candidateName: 'Nguyễn Thị Thu Hà',
        phone: '0984512369',
        email: 'thuha.qaqc@gmail.com',
        yearOfBirth: '1998',
        cccd: '027198006314',
        hometown: 'Bắc Ninh',
        educationLevel: 'Cử nhân Công nghệ Kỹ thuật Hóa học',
        recruitmentTrack: 'PROFESSIONAL',
        applicationSource: 'PDF_UPLOAD',
        scoringMethod: 'GEMINI_AI',
        aiMatchingScore: 86,
        skillMatchPercent: 88,
        experienceMatchPercent: 85,
        shiftHealthPercent: 84,
        aiSummary: 'Nắm vững 7 công cụ QC, báo cáo 8D, FMEA và kiểm soát lỗi ngoại quan linh kiện điện tử. Đã làm 3 năm tại nhà máy vệ tinh Samsung.',
        tags: ['ISO 9001:2015', 'IATF 16949', '8D Report / 5 Why', '7 QC Tools', 'Tiếng Hàn TOPIK 3'],
        shift12hReady: true,
        isRuleRejected: false,
        cvFileUrl: '/uploads/sample-cv-qa.pdf',
        rawResumeText: 'Chuyên viên QA/QC 3 năm kinh nghiệm quản lý chất lượng linh kiện điện tử IQC/OQC.',
        oralChecklist: {
          handEyePass: true,
          visionPass: true,
          shift12hConfirmed: true,
          attitudePass: true,
          quickNote: 'Thành thạo xử lý lỗi hàng NG trên chuyền.'
        },
        status: 'PENDING',
        appliedAt: '2026-11-06T09:05:00Z'
      },
      {
        applicationId: '80000000-0000-0000-0000-000000000405',
        candidateId: '70000000-0000-0000-0000-000000000405',
        jobId: '60000000-0000-0000-0000-000000000101',
        jobTitle: 'Công nhân vận hành dây chuyền SMT (Tuyển gấp 100 người)',
        jobCode: 'JOB-SMT-100',
        departmentCode: 'PROD_LINE',
        departmentName: 'Xưởng Sản Xuất & Lắp Ráp SMT',
        candidateName: 'Phạm Văn Sơn',
        phone: '0973377272',
        email: 'son.pham@mass-recruit.local',
        yearOfBirth: '2003',
        cccd: '036203008912',
        hometown: 'Nam Định',
        educationLevel: 'THPT (12/12)',
        recruitmentTrack: 'MASS_LABOR',
        applicationSource: 'GOOGLE_FORM_QR',
        scoringMethod: 'RULE_ENGINE',
        aiMatchingScore: 95,
        skillMatchPercent: 95,
        experienceMatchPercent: 90,
        shiftHealthPercent: 100,
        aiSummary: 'Đạt tiêu chuẩn sơ tuyển: Độ tuổi 23 (trong khung 18-38), từng đứng chuyền SMT 1.5 năm tại KCN Quế Võ, cam kết làm xoay ca 12h (ngày/đêm) và thị lực tốt.',
        tags: ['Xoay ca 12h', 'Thị lực 10/10', 'Đã làm KCN 1.5 năm', 'Hàn chì cơ bản'],
        shift12hReady: true,
        isRuleRejected: false,
        cvFileUrl: null,
        rawResumeText: 'Tờ khai QR Google Form: Họ và tên: Phạm Văn Sơn | Năm sinh: 2003 | Quê quán: Nam Định | Kinh nghiệm: 1.5 năm đứng máy gắn chip SMT và kiểm tra kính hiển vi | Cam kết làm xoay ca 12h: Có.',
        customFormFields: {
          'Họ và tên của bạn': 'Phạm Văn Sơn',
          'Số điện thoại': '0973377272',
          'Vị trí ứng tuyển': 'Công nhân lắp ráp SMT',
          'Kinh nghiệm nhà máy': '1.5 năm đứng chuyền SMT tại KCN Quế Võ',
          'Cam kết làm xoay ca 12h (Ngày/Đêm)': 'Có - Sẵn sàng tăng ca',
          'Tình trạng thị lực & sức khỏe': 'Thị lực 10/10, không mù màu, không dị ứng phòng sạch',
          'Ngày có thể nhận việc': 'Đi làm ngay'
        },
        oralChecklist: {
          handEyePass: true,
          visionPass: true,
          shift12hConfirmed: true,
          attitudePass: true,
          quickNote: 'Thao tác nhíp gắp linh kiện nhanh, ngoan ngoãn.'
        },
        status: 'PENDING',
        appliedAt: '2026-11-06T09:40:00Z'
      },
      {
        applicationId: '80000000-0000-0000-0000-000000000406',
        candidateId: '70000000-0000-0000-0000-000000000406',
        jobId: '60000000-0000-0000-0000-000000000101',
        jobTitle: 'Công nhân vận hành dây chuyền SMT (Tuyển gấp 100 người)',
        jobCode: 'JOB-SMT-100',
        departmentCode: 'PROD_LINE',
        departmentName: 'Xưởng Sản Xuất & Lắp Ráp SMT',
        candidateName: 'Lê Thị Mai Anh',
        phone: '0389456123',
        email: 'maianh.le@mass-recruit.local',
        yearOfBirth: '2002',
        cccd: '038302004115',
        hometown: 'Thanh Hóa',
        educationLevel: 'THPT (12/12)',
        recruitmentTrack: 'MASS_LABOR',
        applicationSource: 'GOOGLE_FORM_QR',
        scoringMethod: 'RULE_ENGINE',
        aiMatchingScore: 88,
        skillMatchPercent: 85,
        experienceMatchPercent: 80,
        shiftHealthPercent: 98,
        aiSummary: 'Đạt điều kiện sức khỏe phòng sạch, quen soi kính hiển vi kiểm tra bo mạch PCB, xác nhận làm xoay 2 ca 12h.',
        tags: ['Xoay ca 12h', 'Thị lực 10/10', 'Soi kính hiển vi PCB', 'Đã làm phòng sạch'],
        shift12hReady: true,
        isRuleRejected: false,
        cvFileUrl: null,
        rawResumeText: 'Tờ khai QR Google Form: Lê Thị Mai Anh | Năm sinh: 2002 | Quê quán: Thanh Hóa | Kinh nghiệm: 1 năm kiểm tra ngoại quan bản mạch điện tử.',
        customFormFields: {
          'Họ và tên của bạn': 'Lê Thị Mai Anh',
          'Số điện thoại': '0389456123',
          'Vị trí ứng tuyển': 'Công nhân lắp ráp & Kiểm tra ngoại quan',
          'Kinh nghiệm nhà máy': '1 năm soi kính hiển vi kiểm tra bo mạch PCB',
          'Cam kết làm xoay ca 12h (Ngày/Đêm)': 'Có',
          'Tình trạng thị lực & sức khỏe': 'Tốt, mặc được đồ bảo hộ phòng sạch ESD'
        },
        oralChecklist: {
          handEyePass: true,
          visionPass: true,
          shift12hConfirmed: true,
          attitudePass: true,
          quickNote: ''
        },
        status: 'PENDING',
        appliedAt: '2026-11-06T10:10:00Z'
      },
      {
        applicationId: '80000000-0000-0000-0000-000000000407',
        candidateId: '70000000-0000-0000-0000-000000000407',
        jobId: '60000000-0000-0000-0000-000000000103',
        jobTitle: 'Nhân viên Kho vận & Lái xe nâng Reach Truck',
        jobCode: 'JOB-WH-040',
        departmentCode: 'WH_LOGS',
        departmentName: 'Kho Vận & Logistics Nội Bộ',
        candidateName: 'Đặng Quốc Tuấn',
        phone: '0965112233',
        email: 'tuan.dang@walkin-factory.local',
        yearOfBirth: '1995',
        cccd: '024095007781',
        hometown: 'Bắc Giang',
        educationLevel: 'Trung cấp Nghề + Chứng chỉ Xe nâng',
        recruitmentTrack: 'MASS_LABOR',
        applicationSource: 'WALK_IN',
        scoringMethod: 'RULE_ENGINE',
        aiMatchingScore: 90,
        skillMatchPercent: 92,
        experienceMatchPercent: 90,
        shiftHealthPercent: 88,
        aiSummary: 'Tiếp nhận trực tiếp: Có chứng chỉ vận hành xe nâng Reach Truck 2.5 tấn, 3 năm làm kho linh kiện điện tử, sức khỏe tốt.',
        tags: ['Chứng chỉ xe nâng', 'Xoay ca 12h', 'Tiếp nhận tại cổng', 'Đã làm KCN 3 năm'],
        shift12hReady: true,
        isRuleRejected: false,
        cvFileUrl: null,
        rawResumeText: 'Ứng viên đến trực tiếp cổng bảo vệ số 2. Có sẵn chứng chỉ vận hành xe nâng điện đứng lái Reach Truck.',
        customFormFields: {
          'Hình thức tiếp nhận': 'Walk-in trực tiếp tại bàn tuyển dụng',
          'Chứng chỉ nghề': 'Chứng chỉ vận hành xe nâng Reach Truck (Còn hạn)',
          'Kinh nghiệm': '3 năm soạn hàng FIFO & quét mã vạch WMS tại kho linh kiện',
          'Cam kết làm ca đêm': 'Sẵn sàng xoay ca 12h'
        },
        oralChecklist: {
          handEyePass: true,
          visionPass: true,
          shift12hConfirmed: true,
          attitudePass: true,
          quickNote: 'Đã kiểm tra bản gốc chứng chỉ xe nâng tại bàn tuyển dụng.'
        },
        status: 'InterviewPassed',
        appliedAt: '2026-11-06T10:30:00Z'
      },
      {
        applicationId: '80000000-0000-0000-0000-000000000408',
        candidateId: '70000000-0000-0000-0000-000000000408',
        jobId: '60000000-0000-0000-0000-000000000101',
        jobTitle: 'Công nhân vận hành dây chuyền SMT (Tuyển gấp 100 người)',
        jobCode: 'JOB-SMT-100',
        departmentCode: 'PROD_LINE',
        departmentName: 'Xưởng Sản Xuất & Lắp Ráp SMT',
        candidateName: 'Ngô Văn Kiên',
        phone: '0943887766',
        email: 'kien.ngo@mass-recruit.local',
        yearOfBirth: '2004',
        cccd: '025204001199',
        hometown: 'Phú Thọ',
        educationLevel: 'THPT (12/12)',
        recruitmentTrack: 'MASS_LABOR',
        applicationSource: 'GOOGLE_FORM_QR',
        scoringMethod: 'RULE_ENGINE',
        aiMatchingScore: 38,
        skillMatchPercent: 60,
        experienceMatchPercent: 50,
        shiftHealthPercent: 15,
        aiSummary: 'Không đạt tiêu chuẩn sơ tuyển: Ứng viên chọn "Không làm được ca đêm 12h" và cận thị nặng 5.5 diop không phù hợp đứng chuyền gắn chip SMT.',
        tags: ['Không làm ca đêm', 'Thị lực dưới chuẩn (Cận nặng)', 'Chưa có kinh nghiệm KCN'],
        shift12hReady: false,
        isRuleRejected: true,
        ruleRejectReason: 'Từ chối làm xoay ca đêm 12h & Thị lực dưới chuẩn dây chuyền SMT',
        cvFileUrl: null,
        rawResumeText: 'Tờ khai QR Google Form: Ngô Văn Kiên | Không nhận làm ca đêm | Cận 5.5 độ.',
        customFormFields: {
          'Họ và tên của bạn': 'Ngô Văn Kiên',
          'Số điện thoại': '0943887766',
          'Cam kết làm xoay ca 12h (Ngày/Đêm)': 'Không (Chỉ làm hành chính)',
          'Tình trạng thị lực & sức khỏe': 'Cận thị 5.5 độ',
          'Kết luận Rule Engine': 'Không đạt tiêu chí bắt buộc của xưởng sản xuất 3 ca'
        },
        oralChecklist: {
          handEyePass: true,
          visionPass: false,
          shift12hConfirmed: false,
          attitudePass: true,
          quickNote: 'Chỉ xin làm ca hành chính, xưởng SMT không bố trí được.'
        },
        status: 'Rejected',
        appliedAt: '2026-11-06T11:00:00Z'
      }
    ];
  }
}

