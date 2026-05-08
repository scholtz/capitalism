import { faker } from '@faker-js/faker'

const MAX_PERSONAL_ACCOUNT_NAME_LENGTH = 30

export function generatePersonalAccountName() {
  for (let attempt = 0; attempt < 20; attempt += 1) {
    const firstName = faker.person.firstName()
    const middleName = faker.person.firstName()
    const lastName = faker.person.lastName()
    const candidate = `${firstName} ${middleName} ${lastName}`

    if (candidate.length <= MAX_PERSONAL_ACCOUNT_NAME_LENGTH) {
      return candidate
    }
  }

  const shortFirstName = faker.person.firstName().slice(0, 8)
  const shortMiddleName = faker.person.firstName().slice(0, 8)
  const remainingLastNameLength = Math.max(
    1,
    MAX_PERSONAL_ACCOUNT_NAME_LENGTH - shortFirstName.length - shortMiddleName.length - 2,
  )
  const shortLastName = faker.person.lastName().slice(0, remainingLastNameLength)

  return `${shortFirstName} ${shortMiddleName} ${shortLastName}`.slice(0, MAX_PERSONAL_ACCOUNT_NAME_LENGTH)
}
