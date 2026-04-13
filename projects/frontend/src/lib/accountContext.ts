import type { AccountContextType, Company, Player } from '@/types'

type PlayerAccountSnapshot = Pick<Player, 'displayName' | 'activeAccountType' | 'activeCompanyId'> | null | undefined
type CompanyAccountSnapshot = Pick<Company, 'id' | 'name' | 'cash'>

export interface AccountOption<TCompany extends CompanyAccountSnapshot = CompanyAccountSnapshot> {
  key: string
  accountType: AccountContextType
  companyId: string | null
  name: string
  cash: number | null
  company: TCompany | null
  isActive: boolean
}

export function getActiveCompany<TCompany extends CompanyAccountSnapshot>(player: PlayerAccountSnapshot, companies: readonly TCompany[]): TCompany | null {
  if (!player || player.activeAccountType !== 'COMPANY' || !player.activeCompanyId) {
    return null
  }

  return companies.find((company) => company.id === player.activeCompanyId) ?? null
}

export function getPreferredCompany<TCompany extends CompanyAccountSnapshot>(player: PlayerAccountSnapshot, companies: readonly TCompany[]): TCompany | null {
  return getActiveCompany(player, companies) ?? companies[0] ?? null
}

export function getActiveAccountName<TCompany extends CompanyAccountSnapshot>(player: PlayerAccountSnapshot, companies: readonly TCompany[]): string | null {
  const activeCompany = getActiveCompany(player, companies)
  if (activeCompany) {
    return activeCompany.name
  }

  return player?.displayName ?? null
}

export function getActiveAccountOption<TCompany extends CompanyAccountSnapshot>(player: PlayerAccountSnapshot, companies: readonly TCompany[]): AccountOption<TCompany> | null {
  return buildAccountOptions(player, companies).find((option) => option.isActive) ?? null
}

export function buildAccountOptions<TCompany extends CompanyAccountSnapshot>(player: PlayerAccountSnapshot, companies: readonly TCompany[]): AccountOption<TCompany>[] {
  const isCompanyAccountActive = player?.activeAccountType === 'COMPANY' && !!player.activeCompanyId

  return [
    {
      key: 'person',
      accountType: 'PERSON',
      companyId: null,
      name: player?.displayName ?? '',
      cash: null,
      company: null,
      isActive: !isCompanyAccountActive,
    },
    ...companies.map((company) => ({
      key: `company-${company.id}`,
      accountType: 'COMPANY' as const,
      companyId: company.id,
      name: company.name,
      cash: company.cash,
      company,
      isActive: isCompanyAccountActive && player?.activeCompanyId === company.id,
    })),
  ]
}
