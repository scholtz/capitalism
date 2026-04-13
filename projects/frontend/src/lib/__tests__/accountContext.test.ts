import { describe, expect, it } from 'vitest'
import { buildAccountOptions, getActiveAccountName, getActiveAccountOption, getActiveCompany, getPreferredCompany } from '../accountContext'

const companies = [
  { id: 'company-1', name: 'Alpha Manufacturing', cash: 240000 },
  { id: 'company-2', name: 'Bravo Foods', cash: 175000 },
]

describe('accountContext', () => {
  it('returns the active company when a company account is selected', () => {
    const activeCompany = getActiveCompany(
      {
        displayName: 'Alice Founder',
        activeAccountType: 'COMPANY',
        activeCompanyId: 'company-2',
      },
      companies,
    )

    expect(activeCompany).toEqual(companies[1])
  })

  it('returns null when the player is in personal mode', () => {
    const activeCompany = getActiveCompany(
      {
        displayName: 'Alice Founder',
        activeAccountType: 'PERSON',
        activeCompanyId: null,
      },
      companies,
    )

    expect(activeCompany).toBeNull()
  })

  it('falls back to the player display name for the active account label in person mode', () => {
    expect(
      getActiveAccountName(
        {
          displayName: 'Alice Founder',
          activeAccountType: 'PERSON',
          activeCompanyId: null,
        },
        companies,
      ),
    ).toBe('Alice Founder')
  })

  it('uses the company name for the active account label in company mode', () => {
    expect(
      getActiveAccountName(
        {
          displayName: 'Alice Founder',
          activeAccountType: 'COMPANY',
          activeCompanyId: 'company-1',
        },
        companies,
      ),
    ).toBe('Alpha Manufacturing')
  })

  it('returns the active account option including cash and account type', () => {
    expect(
      getActiveAccountOption(
        {
          displayName: 'Alice Founder',
          activeAccountType: 'COMPANY',
          activeCompanyId: 'company-2',
        },
        companies,
      ),
    ).toEqual({
      key: 'company-company-2',
      accountType: 'COMPANY',
      companyId: 'company-2',
      name: 'Bravo Foods',
      cash: 175000,
      company: companies[1],
      isActive: true,
    })
  })

  it('prefers the active company over the first company', () => {
    expect(
      getPreferredCompany(
        {
          displayName: 'Alice Founder',
          activeAccountType: 'COMPANY',
          activeCompanyId: 'company-2',
        },
        companies,
      ),
    ).toEqual(companies[1])
  })

  it('falls back to the first company when no company account is active', () => {
    expect(
      getPreferredCompany(
        {
          displayName: 'Alice Founder',
          activeAccountType: 'PERSON',
          activeCompanyId: null,
        },
        companies,
      ),
    ).toEqual(companies[0])
  })

  it('builds the personal and company account options with the active flag', () => {
    const options = buildAccountOptions(
      {
        displayName: 'Alice Founder',
        activeAccountType: 'COMPANY',
        activeCompanyId: 'company-1',
      },
      companies,
    )

    expect(options).toEqual([
      {
        key: 'person',
        accountType: 'PERSON',
        companyId: null,
        name: 'Alice Founder',
        cash: null,
        company: null,
        isActive: false,
      },
      {
        key: 'company-company-1',
        accountType: 'COMPANY',
        companyId: 'company-1',
        name: 'Alpha Manufacturing',
        cash: 240000,
        company: companies[0],
        isActive: true,
      },
      {
        key: 'company-company-2',
        accountType: 'COMPANY',
        companyId: 'company-2',
        name: 'Bravo Foods',
        cash: 175000,
        company: companies[1],
        isActive: false,
      },
    ])
  })
})
