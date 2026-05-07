import { faker } from '@faker-js/faker'

export function generatePersonalAccountName() {
  const firstName = faker.person.firstName()
  const middleName = faker.person.firstName()
  const lastName = faker.person.lastName()

  return `${firstName} ${middleName} ${lastName}`
}
